using VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelRepository : IRepository<ParcelEntity, Guid>
{
    Task<IReadOnlyList<PlatformParcelReportItem>> GetPlatformParcelMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
        => throw new NotSupportedException("Platform Parcel report is not implemented by this repository.");

    Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default);

    // Payment deposit transitions (PENDING_PAYMENT)
    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositSucceededAsync(
        Guid parcelId, long depositAmount, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> GetPaymentTransitionSnapshotAsync(
        Guid parcelId, CancellationToken ct);

    Task<bool> TrySetPendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType actionType,
        string reason,
        Money? refundAmount,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryResolvePendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType expectedActionType,
        DateTimeOffset now,
        CancellationToken ct);

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
    Task<ParcelPaymentTransitionSnapshot?> TryMarkAdditionalExpiredByDeadlineAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    // Operator review transitions (PENDING_OPERATOR_REVIEW)
    Task<ParcelPaymentTransitionSnapshot?> TryApproveReviewAsync(
        Guid parcelId, Guid reviewedByUserId, Money depositAmount, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryRejectReviewAsync(
        Guid parcelId, Guid reviewedByUserId, string reason, DateTimeOffset now, CancellationToken ct);

    // Reweigh transition (PENDING)
    Task<ParcelPaymentTransitionSnapshot?> TryReweighNoFeeAsync(
        Guid parcelId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money totalPrice,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReweighWithFeeAsync(
        Guid parcelId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money totalPrice,
        Money additionalAmount,
        DateTimeOffset deadline,
        DateTimeOffset now,
        CancellationToken ct);

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
    Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectReviewAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Auto-reject pending parcel when trip is IN_PROGRESS + 30min window passed.
    /// Guards Status==PENDING.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectPendingAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkEscalatePendingTransfersAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkInitiateReturnForRejectedDeliveriesAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetPendingOperatorActionForExpiredConfirmationsAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkReissueDeliveryPendingConfirmRemindersAsync(
        DateTimeOffset expiryCutoff, DateTimeOffset reminderCutoff, DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkExpireOrphanPendingPaymentsAsync(
        DateTimeOffset cutoff, DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkRealertPendingOperatorActionAsync(
        DateTimeOffset cutoff,
        DateTimeOffset reminderCutoff,
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct);

    // ---- Phase 6: Loading / Unloading ----

    /// <summary>
    /// Atomic: PENDING -> LOADED. Guards Status==PENDING, TripId, ParcelCode.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkLoadedAsync(
        Guid parcelId, Guid tripId, string parcelCode, Guid? loadedByUserId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: IN_TRANSIT -> UNLOADED. Clears delivery-token fields.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkUnloadedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: UNLOADED -> DELIVERED_PENDING_CONFIRM with token generation.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkDeliveredPendingConfirmAsync(
        Guid parcelId, Guid deliveryToken, DateTimeOffset deliveryTokenExpiresAt, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Bulk: LOADED -> IN_TRANSIT for all parcels on a trip (trip.started).
    /// </summary>
    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetInTransitByTripIdAsync(
        Guid tripId,
        DateTimeOffset actualDepartureTime,
        CancellationToken ct);

    /// <summary>
    /// Bulk: LOADED/IN_TRANSIT -> PENDING_OPERATOR_ACTION for all unresolved parcels on a trip (trip.completed).
    /// </summary>
    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkSetPendingOperatorActionByTripIdAsync(Guid tripId, DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryBulkRequestTransferByTripIdAsync(
        Guid oldTripId,
        Guid newTripId,
        DateTimeOffset now,
        CancellationToken ct);

    // ---- Phase 8: Operational recovery ----

    Task<ParcelPaymentTransitionSnapshot?> TryRequestTransferAsync(
        Guid parcelId, Guid operatorId, Guid targetTripId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryConfirmTransferAsync(
        Guid parcelId, Guid targetTripId, string parcelCode, Guid confirmedByUserId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReturnAsync(
        Guid parcelId, Guid operatorId, Guid returnedByUserId, string reason, DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryRejectPreAcceptanceByTripIdAsync(
        Guid tripId, DateTimeOffset now, CancellationToken ct);

    Task<IReadOnlyList<ParcelEventSnapshot>> TryCancelPendingByTripIdAsync(
        Guid tripId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryManualCancelAsync(
        Guid parcelId,
        Guid operatorId,
        ParcelStatus targetStatus,
        string reason,
        DateTimeOffset now,
        CancellationToken ct);

    // ---- Phase 6: Queries ----

    /// <summary>
    /// Paginated list of parcels where userId is RecipientUserId.
    /// </summary>
    Task<PagedResult<ParcelEntity>> ListReceivedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken ct);

    Task<PagedResult<ParcelEntity>> ListSentByUserIdAsync(
        Guid userId,
        ParcelStatus? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct)
        => throw new NotSupportedException("Sent parcel history is not implemented by this repository.");

    /// <summary>
    /// Paginated list of non-deleted parcels for an operator's trip.
    /// </summary>
    Task<PagedResult<ParcelEntity>> ListByTripAndOperatorAsync(
        Guid tripId, Guid operatorId, int page, int pageSize, CancellationToken ct);

    // ---- Phase 7: Delivery Token ----

    /// <summary>
    /// Find a parcel by its delivery token (non-revoked, non-expired check is done in handler).
    /// </summary>
    Task<ParcelEntity?> FindByDeliveryTokenAsync(Guid token, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_CONFIRMED.
    /// Guards Status, DeliveryToken, DeliveryTokenRevokedAt==null.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryConfirmDeliveryAsync(
        Guid parcelId, Guid token, string ip, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_REJECTED.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryRejectDeliveryAsync(
        Guid parcelId, Guid token, string reason, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERY_REJECTED -> DELIVERED_PENDING_CONFIRM within undo window.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryUndoRejectDeliveryAsync(
        Guid parcelId, Guid token, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_CONFIRMED by operator/assistant.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryManualConfirmDeliveryAsync(
        Guid parcelId, Guid operatorId, Guid actorUserId, string note, DateTimeOffset now, CancellationToken ct);
}
