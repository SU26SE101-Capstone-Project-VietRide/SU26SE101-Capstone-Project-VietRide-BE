using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

public sealed class RefundToWalletCommandHandler : IRequestHandler<RefundToWalletCommand, RefundToWalletResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWalletRepository _wallets;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IPaymentRepository _payments;
    private readonly IRevenueLedgerWriter _revenueLedger;

    public RefundToWalletCommandHandler(
        IWalletRepository wallets,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IPaymentRepository payments,
        IRevenueLedgerWriter revenueLedger)
    {
        _wallets = wallets;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _payments = payments;
        _revenueLedger = revenueLedger;
    }

    public async Task<RefundToWalletResult> Handle(
        RefundToWalletCommand request,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var amount = Money.FromRaw(request.Amount);

        await _wallets.AcquireWalletTransactionReferenceLockAsync(
                referenceType,
                request.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await _wallets.FindTransactionByReferenceAsync(
                referenceType,
                request.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return ToResult(existing);
        }

        var context = await FindTrustedRefundContextAsync(
            referenceType,
            request.ReferenceId,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "The original payment has no trusted refund context.");
        }

        var allocation = context.Allocations.SingleOrDefault(item => item.ReferenceId == request.ReferenceId)
            ?? throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "The refund reference is missing from original payment context.");
        var paidAmount = checked(
            allocation.GrossAmount
            - allocation.VoucherVietRideFundedAmount
            - allocation.VoucherOperatorFundedAmount);
        if (request.Amount > paidAmount)
        {
            throw new CodedValidationException(
                "REFUND_AMOUNT_EXCEEDS_PAYMENT",
                "Refund amount exceeds the paid allocation amount.");
        }

        var creditedEvent = new WalletCreditedIntegrationEvent(
            request.UserId,
            amount.Amount,
            referenceType.ToString(),
            request.ReferenceId);

        await DebitPlatformWalletAsync(amount, referenceType, request.ReferenceId, cancellationToken).ConfigureAwait(false);
        var transaction = referenceType == WalletTransactionRef.PARCEL_REFUND
            ? await _wallets.CreditRefundAsync(
                    request.UserId,
                    amount,
                    referenceType,
                    request.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false)
            : await _wallets.CreditBookingRefundAsync(
                    request.UserId,
                    amount,
                    request.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false);

        await _revenueLedger.RecordRefundAsync(
            creditedEvent.EventId,
            context,
            request.ReferenceId,
            request.Amount,
            cancellationToken).ConfigureAwait(false);
        await EnqueueWalletCreditedAsync(creditedEvent, cancellationToken).ConfigureAwait(false);

        return ToResult(transaction);
    }

    private async Task DebitPlatformWalletAsync(
        Money amount,
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _platformWallets.DebitAsync(
                    amount,
                    referenceType == WalletTransactionRef.PARCEL_REFUND
                        ? PlatformWalletTransactionRef.PARCEL_REFUND
                        : PlatformWalletTransactionRef.BOOKING_REFUND,
                    referenceId,
                    referenceType == WalletTransactionRef.PARCEL_REFUND ? "Parcel refund" : "Booking refund",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new PlatformWalletInsufficientBalanceException(ex.Message);
        }
    }

    private async Task EnqueueWalletCreditedAsync(
        WalletCreditedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        await _outbox.EnqueueAsync(WalletCreditedIntegrationEvent.EventTypeValue, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PaymentContextV1?> FindTrustedRefundContextAsync(
        WalletTransactionRef refundReferenceType,
        Guid allocationReferenceId,
        CancellationToken cancellationToken)
    {
        if (refundReferenceType == WalletTransactionRef.PARCEL_REFUND)
        {
            return await FindParcelRefundContextAsync(
                allocationReferenceId,
                cancellationToken).ConfigureAwait(false);
        }

        var payment = await _payments.FindByReferenceAsync(
            PaymentReferenceType.BOOKING,
            allocationReferenceId,
            cancellationToken).ConfigureAwait(false);
        if (payment is not null)
            return DeserializeContext(payment);

        var allocationId = allocationReferenceId.ToString("D");
        var containedAllocation = JsonSerializer.Serialize(new
        {
            allocations = new[] { new { referenceId = allocationId } },
        });
        payment = await _payments.Query()
            .Where(candidate => candidate.ReferenceType == PaymentReferenceType.BOOKING_GROUP
                && EF.Functions.JsonContains(candidate.Context, containedAllocation))
            .OrderByDescending(candidate => candidate.SucceededAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return payment is null ? null : DeserializeContext(payment);
    }

    private async Task<PaymentContextV1?> FindParcelRefundContextAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var originalPayment = await _payments.FindByReferenceAsync(
            PaymentReferenceType.PARCEL,
            parcelId,
            cancellationToken).ConfigureAwait(false);
        if (originalPayment is null)
            return null;

        var payments = new List<VietRide.Payment.Domain.Entities.Payment> { originalPayment };
        var additionalPayment = await _payments.FindByReferenceAsync(
            PaymentReferenceType.PARCEL_ADDITIONAL,
            parcelId,
            cancellationToken).ConfigureAwait(false);
        if (additionalPayment is not null)
            payments.Add(additionalPayment);

        var allocations = payments
            .Select(DeserializeContext)
            .SelectMany(context => context.Allocations)
            .Where(allocation => allocation.ReferenceId == parcelId
                && allocation.ReferenceType is "PARCEL" or "PARCEL_ADDITIONAL")
            .ToArray();
        if (allocations.Length != payments.Count)
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "Parcel payment context does not match the refund reference.");
        }

        var first = allocations[0];
        if (allocations.Any(allocation =>
            allocation.OperatorId != first.OperatorId || allocation.TripId != first.TripId))
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "Parcel payment contexts disagree on operator or trip ownership.");
        }

        return new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                parcelId,
                "PARCEL",
                first.OperatorId,
                first.TripId,
                allocations.Sum(allocation => checked(allocation.GrossAmount)),
                allocations.Sum(allocation => checked(allocation.VoucherVietRideFundedAmount)),
                allocations.Sum(allocation => checked(allocation.VoucherOperatorFundedAmount))),
        ]);
    }

    private static PaymentContextV1 DeserializeContext(VietRide.Payment.Domain.Entities.Payment payment)
        => PaymentContextCodec.IsMissing(payment.Context)
            ? throw new CodedValidationException(
                "PAYMENT_CONTEXT_INVALID",
                "The original payment has no trusted refund context.")
            : PaymentContextCodec.DeserializeTrusted(payment.Context);

    private static RefundToWalletResult ToResult(WalletTransaction transaction)
        => new(transaction.Id, transaction.BalanceAfter.Amount);

    private static WalletTransactionRef ParseReferenceType(string value)
        => Enum.TryParse<WalletTransactionRef>(value, ignoreCase: false, out var referenceType)
            && referenceType is WalletTransactionRef.BOOKING_REFUND or WalletTransactionRef.PARCEL_REFUND
            ? referenceType
            : throw new CodedValidationException("VALIDATION_ERROR", "Refund supports BOOKING_REFUND or PARCEL_REFUND references only.");
}
