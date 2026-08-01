using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
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
    private readonly IClock? _clock;

    public RefundToWalletCommandHandler(
        IWalletRepository wallets,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IPaymentRepository payments,
        IRevenueLedgerWriter revenueLedger,
        IClock? clock = null)
    {
        _wallets = wallets;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _payments = payments;
        _revenueLedger = revenueLedger;
        _clock = clock;
    }

    public async Task<RefundToWalletResult> Handle(
        RefundToWalletCommand request,
        CancellationToken cancellationToken)
    {
        var referenceType = ParseReferenceType(request.ReferenceType);

        await _wallets.AcquireWalletTransactionReferenceLockAsync(
                referenceType,
                request.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        var source = await FindTrustedRefundSourceAsync(
            referenceType,
            request.ReferenceId,
            request.PaymentId,
            cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The original payment has no trusted refund context.");
        }

        var allocation = source.Context.Allocations.SingleOrDefault(item =>
                item.ReferenceType == (referenceType == WalletTransactionRef.BOOKING_REFUND ? "BOOKING" : "PARCEL")
                && item.ReferenceId == request.ReferenceId)
            ?? throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The refund reference is missing from original payment context.");
        var paidAmount = checked(
            allocation.GrossAmount
            - allocation.VoucherVietRideFundedAmount
            - allocation.VoucherOperatorFundedAmount);
        var refundAmount = request.Amount;
        if (paidAmount < 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The authoritative refund allocation amount cannot be negative.");
        }

        if (source.Payment?.UserId is Guid paymentOwner && paymentOwner != request.UserId)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The refund owner does not match the captured payment owner.");
        }

        if (referenceType == WalletTransactionRef.BOOKING_REFUND
            && source.Payment is { } fundingPayment)
        {
            if (!fundingPayment.UserId.HasValue)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "The funding payment has no authoritative wallet owner.");
            }

            await _payments.AcquireRefundReconciliationLockAsync(
                    fundingPayment.Id,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var useExactCapturedCorrelation = referenceType == WalletTransactionRef.BOOKING_REFUND
            && request.PaymentId.HasValue
            && source.Payment is { Method: PaymentMethod.VNPAY };
        var exactTransactionId = useExactCapturedCorrelation
            ? CreateExactBookingRefundTransactionId(
                source.Payment!.Id,
                request.ReferenceId)
            : (Guid?)null;
        var genericTransactionId = referenceType == WalletTransactionRef.BOOKING_REFUND
            && !request.PaymentId.HasValue
            && source.Payment is not null
            ? CreateGenericBookingRefundTransactionId(
                source.Payment.Id,
                request.ReferenceId)
            : (Guid?)null;
        var bookingProgress = referenceType == WalletTransactionRef.BOOKING_REFUND
            ? await GetBookingRefundProgressAsync(
                source.Payment!.Id,
                request.ReferenceId,
                request.UserId,
                cancellationToken).ConfigureAwait(false)
            : null;
        var alreadyRefunded = bookingProgress?.Amount
            ?? await GetAuthoritativeRefundedTotalAsync(
                referenceType,
                request.ReferenceId,
                request.UserId,
                cancellationToken).ConfigureAwait(false);
        var existing = useExactCapturedCorrelation
            ? bookingProgress?.Transactions.FirstOrDefault(transaction => transaction.Id == exactTransactionId)
            : bookingProgress?.Transactions.FirstOrDefault(transaction => transaction.Id != exactTransactionId);
        if (existing is not null)
        {
            if (existing.UserId != request.UserId)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "The existing refund belongs to a different wallet owner.");
            }

            if (alreadyRefunded > paidAmount)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Existing refunds exceed the authoritative paid allocation amount.");
            }

            if (alreadyRefunded == paidAmount)
            {
                await ReconcileCapturedPaymentAsync(
                    source.Payment,
                    source.Context,
                    currentAllocationReferenceId: null,
                    currentRefundAmount: 0,
                    useExactCapturedCorrelation,
                    cancellationToken).ConfigureAwait(false);
                return ToResult(existing);
            }


            if (!useExactCapturedCorrelation)
            {
                await ReconcileCapturedPaymentAsync(
                    source.Payment,
                    source.Context,
                    request.ReferenceId,
                    currentRefundAmount: 0,
                    useExactCapturedCorrelation,
                    cancellationToken).ConfigureAwait(false);
                return ToResult(existing);
            }

            refundAmount = checked(paidAmount - alreadyRefunded);
        }

        if (genericTransactionId.HasValue)
        {
            await _revenueLedger.RecordGenericBookingRefundEntitlementAsync(
                CreateGenericBookingRefundEntitlementId(
                    source.Payment!.Id,
                    request.ReferenceId),
                source.Context,
                request.ReferenceId,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.PaymentId.HasValue && request.Amount != paidAmount)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Captured-payment refunds must equal the authoritative allocation amount.");
        }

        if (useExactCapturedCorrelation)
        {
            refundAmount = checked(paidAmount - alreadyRefunded);
        }

        if (paidAmount == 0)
        {
            if (!useExactCapturedCorrelation)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Zero refunds require an exact captured Booking payment.");
            }

            await _revenueLedger.RecordCorrelatedBookingRefundAsync(
                exactTransactionId!.Value,
                CreateBookingVoucherRefundAdjustmentId(
                    source.Payment!.Id,
                    request.ReferenceId),
                source.Context,
                request.ReferenceId,
                refundAmount,
                cancellationToken).ConfigureAwait(false);
            await ReconcileCapturedPaymentAsync(
                source.Payment,
                source.Context,
                request.ReferenceId,
                currentRefundAmount: 0,
                useExactCapturedCorrelation,
                cancellationToken).ConfigureAwait(false);

            var wallet = await _wallets.GetUserWalletAsync(
                request.UserId,
                cancellationToken).ConfigureAwait(false);
            return new RefundToWalletResult(
                exactTransactionId.Value,
                wallet?.Balance.Amount ?? 0);
        }


        if (!useExactCapturedCorrelation)
        {
            refundAmount = Math.Min(refundAmount, checked(paidAmount - alreadyRefunded));
        }

        if (refundAmount == 0)
        {
            await ReconcileCapturedPaymentAsync(
                source.Payment,
                source.Context,
                request.ReferenceId,
                currentRefundAmount: 0,
                useExactCapturedCorrelation,
                cancellationToken).ConfigureAwait(false);
            var wallet = await _wallets.GetUserWalletAsync(
                request.UserId,
                cancellationToken).ConfigureAwait(false);
            return new RefundToWalletResult(
                exactTransactionId ?? genericTransactionId ?? Guid.Empty,
                wallet?.Balance.Amount ?? 0);
        }

        if (checked(alreadyRefunded + refundAmount) > paidAmount)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Refund amount exceeds the paid allocation amount.");
        }
        var amount = Money.FromRaw(refundAmount);
        await DebitPlatformWalletAsync(amount, referenceType, request.ReferenceId, cancellationToken).ConfigureAwait(false);
        WalletTransaction transaction;
        if (referenceType == WalletTransactionRef.PARCEL_REFUND)
        {
            transaction = await _wallets.CreditRefundAsync(
                    request.UserId,
                    amount,
                    referenceType,
                    request.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (useExactCapturedCorrelation)
        {
            transaction = await _wallets.CreditBookingRefundAsync(
                    request.UserId,
                    amount,
                    request.ReferenceId,
                    exactTransactionId!.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            transaction = await _wallets.CreditBookingRefundAsync(
                    request.UserId,
                    amount,
                    request.ReferenceId,
                    genericTransactionId!.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var creditedEvent = new WalletCreditedIntegrationEvent(
            request.UserId,
            amount.Amount,
            referenceType.ToString(),
            request.ReferenceId,
            exactTransactionId ?? transaction.Id,
            referenceType == WalletTransactionRef.BOOKING_REFUND
                ? source.Payment?.Id
                : null);
        if (referenceType == WalletTransactionRef.BOOKING_REFUND)
        {
            await _revenueLedger.RecordCorrelatedBookingRefundAsync(
                creditedEvent.EventId,
                CreateBookingVoucherRefundAdjustmentId(
                    source.Payment!.Id,
                    request.ReferenceId),
                source.Context,
                request.ReferenceId,
                refundAmount,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _revenueLedger.RecordRefundAsync(
                creditedEvent.EventId,
                source.Context,
                request.ReferenceId,
                refundAmount,
                cancellationToken).ConfigureAwait(false);
        }
        await EnqueueWalletCreditedAsync(creditedEvent, cancellationToken).ConfigureAwait(false);
        await ReconcileCapturedPaymentAsync(
            source.Payment,
            source.Context,
            request.ReferenceId,
            refundAmount,
            useExactCapturedCorrelation,
            cancellationToken).ConfigureAwait(false);

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

    private async Task<TrustedRefundSource?> FindTrustedRefundSourceAsync(
        WalletTransactionRef refundReferenceType,
        Guid allocationReferenceId,
        Guid? exactPaymentId,
        CancellationToken cancellationToken)
    {
        if (refundReferenceType == WalletTransactionRef.PARCEL_REFUND)
        {
            var parcelContext = await FindParcelRefundContextAsync(
                allocationReferenceId,
                cancellationToken).ConfigureAwait(false);
            return parcelContext is null ? null : new TrustedRefundSource(null, parcelContext);
        }

        if (exactPaymentId.HasValue)
        {
            var exactPayment = await _payments.GetByIdAsync(
                exactPaymentId.Value,
                cancellationToken).ConfigureAwait(false);
            if (exactPayment is null
                || exactPayment.Method != PaymentMethod.VNPAY
                || exactPayment.Status is not (PaymentStatus.SUCCEEDED or PaymentStatus.REFUNDED)
                || !exactPayment.UserId.HasValue
                || exactPayment.ReferenceType is not (
                    PaymentReferenceType.BOOKING or PaymentReferenceType.BOOKING_GROUP))
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "The exact captured payment is not eligible for a Booking refund.");
            }

            var exactContext = DeserializeContext(exactPayment);
            if (!exactContext.Allocations.Any(allocation =>
                allocation.ReferenceType == "BOOKING"
                && allocation.ReferenceId == allocationReferenceId))
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "The exact captured payment does not own the Booking refund allocation.");
            }

            return new TrustedRefundSource(exactPayment, exactContext);
        }

        var candidates = await _payments.ListSucceededBookingFundingPaymentsByAllocationAsync(
            allocationReferenceId,
            cancellationToken).ConfigureAwait(false);
        if (candidates.Count > 1)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The Booking refund has multiple eligible funding payments and must be deferred.");
        }

        var payment = candidates.SingleOrDefault();
        return payment is null ? null : new TrustedRefundSource(payment, DeserializeContext(payment));
    }

    private async Task ReconcileCapturedPaymentAsync(
        VietRide.Payment.Domain.Entities.Payment? payment,
        PaymentContextV1 context,
        Guid? currentAllocationReferenceId,
        long currentRefundAmount,
        bool useExactCapturedCorrelation,
        CancellationToken cancellationToken)
    {
        if (payment is null
            || (useExactCapturedCorrelation && payment.Method != PaymentMethod.VNPAY))
        {
            return;
        }

        foreach (var allocation in context.Allocations)
        {
            if (allocation.ReferenceType != "BOOKING")
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Captured Booking refund context contains a non-Booking allocation.");
            }

            var expected = checked(
                allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount);
            if (expected < 0)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Captured Booking refund context contains a negative net allocation.");
            }

            if (expected == 0)
            {
                if (!useExactCapturedCorrelation)
                {
                    return;
                }

                if (allocation.VoucherVietRideFundedAmount == 0)
                {
                    continue;
                }

                var isCurrentRecordedAllocation =
                    allocation.ReferenceId == currentAllocationReferenceId
                    && currentRefundAmount == 0;
                var zeroRefundRecorded = isCurrentRecordedAllocation
                    || await _revenueLedger.IsRefundRecordedAsync(
                        CreateBookingVoucherRefundAdjustmentId(
                            payment.Id,
                            allocation.ReferenceId),
                        allocation.ReferenceId,
                        cancellationToken).ConfigureAwait(false);
                if (!zeroRefundRecorded)
                {
                    return;
                }

                continue;
            }

            if (!useExactCapturedCorrelation)
            {
                var isCurrentGenericAllocation = allocation.ReferenceId == currentAllocationReferenceId;
                var genericEntitlementRecorded = isCurrentGenericAllocation
                    || await _revenueLedger.IsRefundRecordedAsync(
                        CreateGenericBookingRefundEntitlementId(
                            payment.Id,
                            allocation.ReferenceId),
                        allocation.ReferenceId,
                        cancellationToken).ConfigureAwait(false);
                if (!genericEntitlementRecorded)
                {
                    return;
                }
            }

            var refundProgress = await GetBookingRefundProgressAsync(
                payment.Id,
                allocation.ReferenceId,
                payment.UserId!.Value,
                cancellationToken).ConfigureAwait(false);
            var refunded = refundProgress.Amount;
            if (allocation.ReferenceId == currentAllocationReferenceId)
            {
                var currentTransactionId = useExactCapturedCorrelation
                    ? CreateExactBookingRefundTransactionId(payment.Id, allocation.ReferenceId)
                    : CreateGenericBookingRefundTransactionId(payment.Id, allocation.ReferenceId);
                if (refundProgress.Transactions.All(transaction => transaction.Id != currentTransactionId))
                {
                    refunded = checked(refunded + currentRefundAmount);
                }
            }
            if (refunded > expected)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Booking refunds exceed an authoritative captured-payment allocation.");
            }

            if (refunded < expected)
            {
                return;
            }
        }

        var transitioned = await _payments.TryMarkRefundedByIdAsync(
            payment.Id,
            _clock?.UtcNow ?? DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        if (!transitioned)
        {
            return;
        }

        var refundedEvent = new PaymentRefundedIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount,
            context);
        await _outbox.EnqueueAsync(
            refundedEvent.EventType,
            JsonSerializer.Serialize(refundedEvent, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BookingRefundProgress> GetBookingRefundProgressAsync(
        Guid paymentId,
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
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Refund history contains a transaction owned by a different wallet.");
        }

        var exactTransactionId = CreateExactBookingRefundTransactionId(paymentId, bookingId);
        var genericTransactionId = CreateGenericBookingRefundTransactionId(paymentId, bookingId);
        var matchingTransactions = transactions
            .Where(candidate => candidate.Id == exactTransactionId
                || candidate.Id == genericTransactionId)
            .ToList();
        var paymentAttempts = await _payments.ListBookingPaymentAttemptsByAllocationAsync(
                bookingId,
                cancellationToken)
            .ConfigureAwait(false);
        if (paymentAttempts.Count == 1 && paymentAttempts[0].Id == paymentId)
        {
            var deterministicIds = paymentAttempts
                .SelectMany(payment => new[]
                {
                    CreateExactBookingRefundTransactionId(payment.Id, bookingId),
                    CreateGenericBookingRefundTransactionId(payment.Id, bookingId),
                })
                .ToHashSet();
            matchingTransactions.AddRange(transactions.Where(transaction =>
                !deterministicIds.Contains(transaction.Id)));
        }

        var amount = matchingTransactions.Aggregate(
            0L,
            (total, transaction) => checked(total + transaction.Amount.Amount));
        return new BookingRefundProgress(
            amount,
            matchingTransactions);
    }

    private static Guid CreateExactBookingRefundTransactionId(
        Guid paymentId,
        Guid bookingId)
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

    private static Guid CreateGenericBookingRefundTransactionId(Guid paymentId, Guid bookingId)
    {
        var correlation = Encoding.UTF8.GetBytes(
            $"booking-refund-generic:{paymentId:D}:allocation:{bookingId:D}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(correlation, hash);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static Guid CreateGenericBookingRefundEntitlementId(Guid paymentId, Guid bookingId)
        => CreateBookingRefundCorrelationId(
            "booking-refund-generic-entitlement",
            paymentId,
            bookingId);

    private static Guid CreateBookingVoucherRefundAdjustmentId(Guid paymentId, Guid bookingId)
        => CreateBookingRefundCorrelationId(
            "booking-refund-voucher-adjustment",
            paymentId,
            bookingId);

    private static Guid CreateBookingRefundCorrelationId(
        string phase,
        Guid paymentId,
        Guid bookingId)
    {
        var correlation = Encoding.UTF8.GetBytes(
            $"{phase}:{paymentId:D}:allocation:{bookingId:D}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(correlation, hash);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private async Task<long> GetAuthoritativeRefundedTotalAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var total = await _wallets.GetTotalRefundedByReferenceAsync(
                referenceType,
                referenceId,
                cancellationToken)
            .ConfigureAwait(false);
        var ownerTotal = await _wallets.GetTotalRefundedByReferenceAndUserAsync(
                referenceType,
                referenceId,
                userId,
                cancellationToken)
            .ConfigureAwait(false);
        if (total != ownerTotal)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Refund history contains a transaction owned by a different wallet.");
        }

        return ownerTotal;
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
                "VALIDATION_ERROR",
                "Parcel payment context does not match the refund reference.");
        }

        var first = allocations[0];
        if (allocations.Any(allocation =>
            allocation.OperatorId != first.OperatorId || allocation.TripId != first.TripId))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
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
                "VALIDATION_ERROR",
                "The original payment has no trusted refund context.")
            : PaymentContextCodec.DeserializeTrusted(payment.Context);

    private static RefundToWalletResult ToResult(WalletTransaction transaction)
        => new(transaction.Id, transaction.BalanceAfter.Amount);

    private static WalletTransactionRef ParseReferenceType(string value)
        => Enum.TryParse<WalletTransactionRef>(value, ignoreCase: false, out var referenceType)
            && referenceType is WalletTransactionRef.BOOKING_REFUND or WalletTransactionRef.PARCEL_REFUND
            ? referenceType
            : throw new CodedValidationException("VALIDATION_ERROR", "Refund supports BOOKING_REFUND or PARCEL_REFUND references only.");

    private sealed record TrustedRefundSource(
        VietRide.Payment.Domain.Entities.Payment? Payment,
        PaymentContextV1 Context);

    private sealed record BookingRefundProgress(
        long Amount,
        IReadOnlyList<WalletTransaction> Transactions);
}
