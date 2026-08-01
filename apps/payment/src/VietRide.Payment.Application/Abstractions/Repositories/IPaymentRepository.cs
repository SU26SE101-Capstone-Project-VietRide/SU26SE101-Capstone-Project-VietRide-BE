using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IPaymentRepository : IRepository<PaymentEntity, Guid>
{
    Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<PaymentEntity?> FindByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken);

    Task<PaymentEntity?> FindLatestByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => FindByReferenceAsync(referenceType, referenceId, cancellationToken);

    Task<PaymentEntity?> FindSucceededByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => FindByReferenceAsync(referenceType, referenceId, cancellationToken);

    Task<IReadOnlyList<PaymentEntity>> ListLatestSubscriptionPaymentsAsync(
        IReadOnlyCollection<Guid> upgradeAttemptIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PaymentEntity> result = QueryNoTracking()
            .Where(payment => payment.ReferenceType == PaymentReferenceType.SUBSCRIPTION
                && upgradeAttemptIds.Contains(payment.ReferenceId))
            .AsEnumerable()
            .GroupBy(payment => payment.ReferenceId)
            .Select(group => group.OrderByDescending(payment => payment.CreatedAt).First())
            .ToArray();
        return Task.FromResult(result);
    }

    Task AcquirePaymentReferenceLockAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken);

    Task<PaymentEntity?> FindVnPayPaymentByTxnRefAsync(
        string vnPayTxnRef,
        CancellationToken cancellationToken)
        => Task.FromResult(QueryNoTracking().FirstOrDefault(payment =>
            payment.VnPayTxnRef == vnPayTxnRef
            && payment.Method == PaymentMethod.VNPAY));

    Task<PaymentEntity?> LockAndReloadAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
        => GetByIdAsync(paymentId, cancellationToken);

    Task<WalletTransaction> DebitWalletBookingPaymentAsync(
        Guid userId,
        Guid bookingId,
        Money amount,
        CancellationToken cancellationToken);

    Task<WalletTransaction> DebitWalletPaymentAsync(
        Guid userId,
        Guid referenceId,
        Money amount,
        WalletTransactionRef walletRef,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(
        DateTimeOffset legacyCreatedAtOrBefore,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transitions the SUCCEEDED Payment row for the given reference to REFUNDED
    /// (BSOT §8.4). Returns false when no SUCCEEDED row matched (already refunded / not found) so
    /// the wallet.credited consumer is an idempotent no-op on re-delivery.
    /// </summary>
    Task<bool> TryMarkRefundedByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        DateTimeOffset refundedAt,
        CancellationToken cancellationToken);

    Task<PaymentEntity?> FindSucceededBookingPaymentByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
        => Task.FromResult<PaymentEntity?>(null);

    async Task<IReadOnlyList<PaymentEntity>> ListSucceededBookingFundingPaymentsByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var payment = await FindSucceededBookingPaymentByAllocationAsync(
            bookingId,
            cancellationToken).ConfigureAwait(false);
        return payment is null ? [] : [payment];
    }

    Task<IReadOnlyList<PaymentEntity>> ListBookingPaymentAttemptsByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);

    Task<bool> TryMarkRefundedByIdAsync(
        Guid paymentId,
        DateTimeOffset refundedAt,
        CancellationToken cancellationToken)
        => Task.FromResult(false);

    Task AcquireRefundReconciliationLockAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
