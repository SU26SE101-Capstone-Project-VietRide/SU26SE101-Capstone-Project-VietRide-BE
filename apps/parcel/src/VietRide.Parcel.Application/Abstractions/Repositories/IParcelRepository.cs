using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelRepository : IRepository<ParcelEntity, Guid>
{
    Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default);

    // Payment deposit transitions (PENDING_PAYMENT)
    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositSucceededAsync(
        Guid parcelId, long depositAmount, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositFailedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositExpiredAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    // Additional payment transitions (PENDING_ADDITIONAL_PAYMENT)
    Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalSucceededAsync(
        Guid parcelId, long additionalAmount, Guid paymentId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalFailedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Job path: rejects only when AdditionalPaymentDeadline has passed (atomic guard).
    /// </summary>
    Task<bool> TryMarkAdditionalExpiredByDeadlineAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    // Operator review transitions (PENDING_OPERATOR_REVIEW)
    Task<ParcelPaymentTransitionSnapshot?> TryApproveReviewAsync(
        Guid parcelId, Guid reviewedByUserId, Money depositAmount, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryRejectReviewAsync(
        Guid parcelId, Guid reviewedByUserId, string reason, DateTimeOffset now, CancellationToken ct);

    // Reweigh transition (PENDING)
    Task<ParcelPaymentTransitionSnapshot?> TryReweighNoFeeAsync(
        Guid parcelId, decimal actualWeightKg, ParcelSizeCategory actualSizeCategory, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReweighWithFeeAsync(
        Guid parcelId, decimal actualWeightKg, ParcelSizeCategory actualSizeCategory,
        Money additionalAmount, DateTimeOffset deadline, DateTimeOffset now, CancellationToken ct);

    // Additional payment idempotent assignment (PENDING_ADDITIONAL_PAYMENT)
    Task<bool> TryAssignAdditionalPaymentIdAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct);

    // ---- Hangfire job candidates (lightweight projections) ----

    Task<IReadOnlyList<Guid>> ListReviewTimedOutIdsAsync(
        DateTimeOffset cutoff, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListAdditionalPaymentTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<PendingParcelTripRef>> ListPendingForLoadCheckAsync(
        int maxBatch, CancellationToken ct);

    // ---- Hangfire job atomic transitions ----

    /// <summary>
    /// Auto-reject review (no human reviewer). Guards Status==PENDING_OPERATOR_REVIEW &amp;&amp; ReviewDecision==PENDING.
    /// </summary>
    Task<bool> TryAutoRejectReviewAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Auto-reject pending parcel when trip is IN_PROGRESS + 30min window passed.
    /// Guards Status==PENDING.
    /// </summary>
    Task<bool> TryAutoRejectPendingAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);
}
