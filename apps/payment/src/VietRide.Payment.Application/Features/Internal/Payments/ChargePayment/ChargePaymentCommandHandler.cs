using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed class ChargePaymentCommandHandler : IRequestHandler<ChargePaymentCommand, ChargePaymentResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string WalletMethod = "WALLET";
    private const string VnPayMethod = "VNPAY";

    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IVnPayClient _vnPayClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly IRevenueLedgerWriter _revenueLedger;

    public ChargePaymentCommandHandler(
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IVnPayClient vnPayClient,
        IIntegrationEventOutbox outbox,
        IRevenueLedgerWriter revenueLedger,
        IClock clock)
    {
        _payments = payments;
        _platformWallets = platformWallets;
        _vnPayClient = vnPayClient;
        _outbox = outbox;
        _revenueLedger = revenueLedger;
        _clock = clock;
    }

    public async Task<ChargePaymentResult> Handle(
        ChargePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var method = ParseMethod(request.Method);
        var now = _clock.UtcNow;
        var dueAt = request.DueAt ?? now.AddMinutes(15);
        if (dueAt <= now)
            throw new CodedValidationException("PAYMENT_DEADLINE_PASSED", "Payment dueAt must be in the future.");
        if (referenceType == PaymentReferenceType.BOOKING_GROUP && method != PaymentMethod.VNPAY)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "BOOKING_GROUP is supported by VNPAY only.");
        }

        var amount = Money.FromRaw(request.Amount);
        var contextJson = PaymentContextCodec.ValidateAndSerialize(
            request.Context,
            request.ReferenceType,
            request.ReferenceId,
            request.Amount);

        var replay = await _payments.FindByIdempotencyKeyAsync(request.IdempotencyKey!, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return ToResult(replay);
        }

        await _payments.AcquirePaymentReferenceLockAsync(referenceType, request.ReferenceId, cancellationToken)
            .ConfigureAwait(false);

        var existing = await _payments.FindByReferenceAsync(referenceType, request.ReferenceId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new ConflictException("PAYMENT_ALREADY_PROCESSED", "A payment already exists for the requested reference.");
        }

        return method == PaymentMethod.WALLET
            ? await ChargeWalletAsync(request, amount, contextJson, dueAt, cancellationToken).ConfigureAwait(false)
            : await CreateVnPayPendingRedirectAsync(request, amount, contextJson, dueAt, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChargePaymentResult> ChargeWalletAsync(
        ChargePaymentCommand request,
        Money amount,
        string contextJson,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var now = _clock.UtcNow;
        var payment = PaymentEntity.CreatePendingRedirect(
            referenceType,
            request.ReferenceId,
            amount,
            PaymentMethod.WALLET,
            userId: request.UserId,
            idempotencyKey: request.IdempotencyKey,
            dueAt: dueAt);
        payment.MarkSucceeded(null, now);
        payment.AttachContext(contextJson);

        var (walletRef, platformRef) = MapChargeRefs(referenceType);
        var walletTransaction = await _payments.DebitWalletPaymentAsync(
                request.UserId,
                request.ReferenceId,
                amount,
                walletRef,
                cancellationToken)
            .ConfigureAwait(false);
        await _platformWallets.CreditAsync(
                amount,
                platformRef,
                request.ReferenceId,
                $"{referenceType} payment hold",
                cancellationToken)
            .ConfigureAwait(false);
        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        await EnqueueWalletDebitedAsync(walletTransaction, now, cancellationToken).ConfigureAwait(false);
        await RecordRevenueAndEnqueuePaymentSucceededAsync(payment, cancellationToken).ConfigureAwait(false);

        return ToResult(payment);
    }

    private async Task<ChargePaymentResult> CreateVnPayPendingRedirectAsync(
        ChargePaymentCommand request,
        Money amount,
        string contextJson,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);
        var vnPayTxnRef = Guid.NewGuid().ToString("D");
        var now = _clock.UtcNow;
        var redirectUrl = _vnPayClient.CreateBookingPaymentRedirectUrl(
            request.ReferenceId,
            request.UserId,
            amount,
            vnPayTxnRef,
            request.ClientIpAddress,
            now,
            dueAt);

        var payment = PaymentEntity.CreatePendingRedirectVnPay(
            referenceType,
            request.ReferenceId,
            request.UserId,
            amount,
            vnPayTxnRef,
            request.IdempotencyKey!,
            redirectUrl,
            dueAt);
        payment.AttachContext(contextJson);

        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        return ToResult(payment);
    }

    private static (WalletTransactionRef Wallet, PlatformWalletTransactionRef Platform) MapChargeRefs(
        PaymentReferenceType referenceType) => referenceType switch
        {
            PaymentReferenceType.BOOKING => (WalletTransactionRef.BOOKING_PAYMENT, PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD),
            PaymentReferenceType.PARCEL => (WalletTransactionRef.PARCEL_PAYMENT, PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD),
            PaymentReferenceType.PARCEL_ADDITIONAL => (WalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT, PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD),
            _ => throw new CodedValidationException("VALIDATION_ERROR", $"Unexpected reference type {referenceType}."),
        };

    private async Task RecordRevenueAndEnqueuePaymentSucceededAsync(
        PaymentEntity payment,
        CancellationToken cancellationToken)
    {
        var context = PaymentContextCodec.DeserializeTrusted(payment.Context);
        var evt = new PaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount,
            payment.Method,
            context,
            payment.SucceededAt!.Value,
            payment.DueAt);
        await _revenueLedger.RecordPaymentSucceededAsync(
            evt.EventId,
            context,
            cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
    }

    private Task EnqueueWalletDebitedAsync(
        WalletTransaction transaction,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var evt = new WalletDebitedIntegrationEvent(
            Guid.NewGuid(),
            occurredAt,
            transaction.UserId,
            transaction.Id,
            transaction.Amount.Amount,
            transaction.BalanceAfter.Amount,
            transaction.ReferenceType.ToString(),
            transaction.ReferenceId!.Value);
        return _outbox.EnqueueAsync(
            evt.EventId,
            evt.EventType,
            JsonSerializer.Serialize(evt, JsonOptions),
            cancellationToken);
    }

    private static ChargePaymentResult ToResult(PaymentEntity payment)
        => new(payment.Id, payment.Status.ToString(), payment.PaymentRedirectUrl, payment.DueAt);

    private static PaymentReferenceType ParseReferenceType(string value)
        => Enum.TryParse<PaymentReferenceType>(value, ignoreCase: false, out var referenceType)
            && referenceType is PaymentReferenceType.BOOKING
                or PaymentReferenceType.BOOKING_GROUP
                or PaymentReferenceType.PARCEL
                or PaymentReferenceType.PARCEL_ADDITIONAL
            ? referenceType
            : throw new CodedValidationException("VALIDATION_ERROR",
                "Charge supports BOOKING, BOOKING_GROUP, PARCEL, or PARCEL_ADDITIONAL references only.");

    private static PaymentMethod ParseMethod(string value)
        => Enum.TryParse<PaymentMethod>(value, ignoreCase: false, out var method)
            && method is PaymentMethod.WALLET or PaymentMethod.VNPAY
            ? method
            : throw new CodedValidationException("VALIDATION_ERROR", "method must be WALLET or VNPAY.");
}
