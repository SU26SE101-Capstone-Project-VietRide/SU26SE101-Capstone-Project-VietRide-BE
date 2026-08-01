using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.MarkPaymentRefunded;

/// <summary>
/// Consumes the canonical payment.wallet.credited event and drives the originating Payment row to
/// REFUNDED for refund credits (BSOT §8.4: referenceType ∈ BOOKING_REFUND / PARCEL_REFUND). Other
/// wallet credits (e.g. top-ups) are ignored. Idempotent: the repository transition is status-guarded.
/// </summary>
public sealed class MarkPaymentRefundedCommandHandler : IIntegrationEventHandler<WalletCreditedConsumerEvent>
{
    private const string BookingRefund = "BOOKING_REFUND";
    private const string ParcelRefund = "PARCEL_REFUND";

    private readonly IPaymentRepository _payments;
    private readonly IWalletRepository _wallets;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ILogger<MarkPaymentRefundedCommandHandler> _logger;

    public MarkPaymentRefundedCommandHandler(
        IPaymentRepository payments,
        IWalletRepository wallets,
        IClock clock,
        IIntegrationEventOutbox outbox,
        ILogger<MarkPaymentRefundedCommandHandler> logger)
    {
        _payments = payments;
        _wallets = wallets;
        _clock = clock;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task Handle(MarkPaymentRefundedCommand request, CancellationToken cancellationToken)
    {
        if (!TryMapReferenceType(request.ReferenceType, out var referenceType))
        {
            // Not a refund credit (e.g. a top-up) — nothing to transition.
            return;
        }

        if (referenceType == PaymentReferenceType.BOOKING
            && request.SourceEventId.HasValue
            && request.UserId.HasValue
            && request.Amount.HasValue
            && request.PaymentId.HasValue
            && request.SourceEventId.Value == CreateExactBookingRefundTransactionId(
                request.PaymentId.Value,
                request.ReferenceId))
        {
            var exactTransaction = await _wallets.FindTransactionByIdAsync(
                    request.SourceEventId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exactTransaction is
                {
                    Type: WalletTransactionType.CREDIT,
                    ReferenceType: WalletTransactionRef.BOOKING_REFUND,
                }
                && exactTransaction.ReferenceId == request.ReferenceId
                && exactTransaction.UserId == request.UserId.Value
                && exactTransaction.Amount.Amount == request.Amount.Value)
            {
                _logger.LogDebug(
                    "Ignored exact captured-payment wallet credit {WalletTransactionId} for Booking {BookingId}; Payment reconciliation already committed with the credit.",
                    exactTransaction.Id,
                    request.ReferenceId);
                return;
            }
        }

        if (referenceType == PaymentReferenceType.BOOKING)
        {
            if (!request.UserId.HasValue)
            {
                _logger.LogWarning(
                    "Ignored uncorrelated Booking refund credit {SourceEventId} for Booking {BookingId}.",
                    request.SourceEventId,
                    request.ReferenceId);
                return;
            }

            var correlatedPaymentId = request.PaymentId
                ?? await ResolveLegacyGenericBookingFundingPaymentIdAsync(
                    request,
                    cancellationToken).ConfigureAwait(false);
            if (!correlatedPaymentId.HasValue)
            {
                return;
            }

            await ReconcileGenericBookingRefundAsync(
                correlatedPaymentId.Value,
                request.ReferenceId,
                request.UserId.Value,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var payment = await _payments.FindSucceededByReferenceAsync(
            referenceType,
            request.ReferenceId,
            cancellationToken).ConfigureAwait(false);
        var transitioned = payment is not null
            && await _payments.TryMarkRefundedByIdAsync(
                payment.Id,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);

        if (transitioned)
        {
            if (payment is not null && !PaymentContextCodec.IsMissing(payment.Context))
            {
                var evt = new PaymentRefundedIntegrationEvent(
                    payment.Id,
                    payment.ReferenceType,
                    payment.ReferenceId,
                    payment.Amount.Amount,
                    PaymentContextCodec.DeserializeTrusted(payment.Context));
                var payload = JsonSerializer.Serialize(
                    evt,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Refunded payment for {ReferenceType}/{ReferenceId} has no trusted context; refund fact is quarantined.",
                    request.ReferenceType,
                    request.ReferenceId);
            }

            _logger.LogInformation(
                "Payment for {ReferenceType} {ReferenceId} marked REFUNDED from payment.wallet.credited.",
                request.ReferenceType,
                request.ReferenceId);
        }
        else
        {
            _logger.LogDebug(
                "payment.wallet.credited refund no-op for {ReferenceType} {ReferenceId}; no SUCCEEDED payment to refund.",
                request.ReferenceType,
                request.ReferenceId);
        }
    }

    private async Task<Guid?> ResolveLegacyGenericBookingFundingPaymentIdAsync(
        MarkPaymentRefundedCommand request,
        CancellationToken cancellationToken)
    {
        if (!request.SourceEventId.HasValue
            || !request.UserId.HasValue
            || !request.Amount.HasValue)
        {
            throw new InvalidOperationException(
                "Legacy Booking refund credit is missing wallet transaction correlation.");
        }

        var attempts = await _payments.ListBookingPaymentAttemptsByAllocationAsync(
                request.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);
        var exactTransactionIds = attempts
            .Select(payment =>
                CreateExactBookingRefundTransactionId(payment.Id, request.ReferenceId))
            .ToHashSet();
        var transaction = await _wallets.FindTransactionByIdAsync(
                request.SourceEventId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (transaction is not null && exactTransactionIds.Contains(transaction.Id))
        {
            _logger.LogDebug(
                "Ignored legacy exact captured-payment wallet credit {WalletTransactionId}; reconciliation committed with the credit.",
                transaction.Id);
            return null;
        }

        if (transaction is not null
            && (transaction is not
            {
                Type: WalletTransactionType.CREDIT,
                ReferenceType: WalletTransactionRef.BOOKING_REFUND,
            }
            || transaction.ReferenceId != request.ReferenceId
            || transaction.UserId != request.UserId.Value
            || transaction.Amount.Amount != request.Amount.Value))
        {
            throw new InvalidOperationException(
                "Legacy Booking refund credit does not match its wallet transaction.");
        }

        if (transaction is null)
        {
            var refundTransactions = await _wallets.ListRefundTransactionsByReferenceAsync(
                    WalletTransactionRef.BOOKING_REFUND,
                    request.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false);
            var hasMatchingGenericTransaction = refundTransactions.Any(candidate =>
                !exactTransactionIds.Contains(candidate.Id)
                && candidate.UserId == request.UserId.Value
                && candidate.Amount.Amount == request.Amount.Value);
            if (!hasMatchingGenericTransaction)
            {
                throw new InvalidOperationException(
                    "Legacy Booking refund credit has no matching generic wallet transaction.");
            }
        }

        var candidates = await _payments.ListSucceededBookingFundingPaymentsByAllocationAsync(
                request.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                "Legacy Booking refund credit cannot be correlated to exactly one funding payment.");
        }

        return candidates[0].Id;
    }

    private async Task ReconcileGenericBookingRefundAsync(
        Guid paymentId,
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await _payments.AcquireRefundReconciliationLockAsync(
                paymentId,
                cancellationToken)
            .ConfigureAwait(false);

        var payment = await _payments.GetByIdAsync(paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null
            || payment.Method is not (PaymentMethod.WALLET or PaymentMethod.VNPAY)
            || payment.Status is not (PaymentStatus.SUCCEEDED or PaymentStatus.REFUNDED)
            || !payment.SucceededAt.HasValue
            || payment.UserId != userId
            || payment.ReferenceType is not (
                PaymentReferenceType.BOOKING or PaymentReferenceType.BOOKING_GROUP)
            || PaymentContextCodec.IsMissing(payment.Context))
        {
            _logger.LogWarning(
                "Ignored Booking refund credit for invalid correlated funding Payment {PaymentId}.",
                paymentId);
            return;
        }

        var context = PaymentContextCodec.DeserializeTrusted(payment.Context);
        if (!context.Allocations.Any(allocation =>
                allocation.ReferenceType == "BOOKING"
                && allocation.ReferenceId == bookingId))
        {
            _logger.LogWarning(
                "Ignored Booking refund credit because Payment {PaymentId} does not own Booking {BookingId}.",
                paymentId,
                bookingId);
            return;
        }

        foreach (var allocation in context.Allocations)
        {
            if (allocation.ReferenceType != "BOOKING")
            {
                _logger.LogWarning(
                    "Ignored Booking refund credit because Payment {PaymentId} has a non-Booking allocation.",
                    paymentId);
                return;
            }

            var expected = checked(
                allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount);
            var refunded = await GetGenericBookingRefundedAmountAsync(
                allocation.ReferenceId,
                userId,
                cancellationToken).ConfigureAwait(false);
            var allocationIsReconciled = payment.ReferenceType == PaymentReferenceType.BOOKING
                ? refunded > 0 && refunded <= expected
                : refunded == expected;
            if (expected <= 0 || !allocationIsReconciled)
            {
                if (refunded > expected)
                {
                    _logger.LogWarning(
                        "Generic Booking refunds {RefundedAmount} exceed trusted allocation {ExpectedAmount} for Payment {PaymentId}, Booking {BookingId}.",
                        refunded,
                        expected,
                        paymentId,
                        allocation.ReferenceId);
                }

                return;
            }
        }

        var transitioned = await _payments.TryMarkRefundedByIdAsync(
            payment.Id,
            _clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (!transitioned)
        {
            _logger.LogDebug(
                "Correlated funding Payment {PaymentId} was already reconciled.",
                payment.Id);
            return;
        }

        var evt = new PaymentRefundedIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount,
            context);
        var payload = JsonSerializer.Serialize(
            evt,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Correlated funding Payment {PaymentId} marked REFUNDED from payment.wallet.credited.",
            payment.Id);
    }

    private async Task<long> GetGenericBookingRefundedAmountAsync(
        Guid bookingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var transactions = await _wallets.ListRefundTransactionsByReferenceAsync(
                WalletTransactionRef.BOOKING_REFUND,
                bookingId,
                cancellationToken)
            .ConfigureAwait(false);
        if (transactions.Any(transaction => transaction.UserId != userId))
        {
            return -1;
        }

        var paymentAttempts = await _payments.ListBookingPaymentAttemptsByAllocationAsync(
                bookingId,
                cancellationToken)
            .ConfigureAwait(false);
        var exactTransactionIds = paymentAttempts
            .Select(payment => CreateExactBookingRefundTransactionId(payment.Id, bookingId))
            .ToHashSet();
        return transactions
            .Where(transaction => !exactTransactionIds.Contains(transaction.Id))
            .Aggregate(
                0L,
                (total, transaction) => checked(total + transaction.Amount.Amount));
    }

    private static Guid CreateExactBookingRefundTransactionId(Guid paymentId, Guid bookingId)
    {
        var correlation = Encoding.UTF8.GetBytes(
            $"booking-refund-payment:{paymentId:D}:allocation:{bookingId:D}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(correlation, hash);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    public async Task HandleAsync(WalletCreditedConsumerEvent integrationEvent, CancellationToken cancellationToken)
        => await Handle(
            new MarkPaymentRefundedCommand(
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
                integrationEvent.EventId,
                integrationEvent.UserId,
                integrationEvent.Amount,
                integrationEvent.PaymentId),
            cancellationToken);

    private static bool TryMapReferenceType(string referenceType, out PaymentReferenceType mapped)
    {
        switch (referenceType)
        {
            case BookingRefund:
                mapped = PaymentReferenceType.BOOKING;
                return true;
            case ParcelRefund:
                mapped = PaymentReferenceType.PARCEL;
                return true;
            default:
                mapped = default;
                return false;
        }
    }
}
