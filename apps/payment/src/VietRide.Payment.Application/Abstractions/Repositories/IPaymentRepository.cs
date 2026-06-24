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

    Task AcquirePaymentReferenceLockAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken);

    Task<WalletTransaction> DebitWalletBookingPaymentAsync(
        Guid userId,
        Guid bookingId,
        Money amount,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(
        DateTimeOffset expiresBefore,
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
}
