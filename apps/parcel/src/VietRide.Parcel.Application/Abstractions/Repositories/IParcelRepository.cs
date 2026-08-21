using VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;
using VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;
using VietRide.Parcel.Application.Features.Parcels.OperatorDetail;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelRepository : IRepository<ParcelEntity, Guid>
{
    Task<OperatorParcelDetailData?> GetOperatorDetailAsync(
        Guid parcelId,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator Parcel detail is not implemented by this repository.");

    Task<IReadOnlyList<ParcelTripDisplaySnapshotCandidate>> ListTripDisplaySnapshotBackfillCandidatesAsync(
        int batchSize,
        CancellationToken ct = default)
        => throw new NotSupportedException("Parcel trip display snapshot backfill is not implemented by this repository.");

    Task<int> ApplyTripDisplaySnapshotBackfillAsync(
        IReadOnlyCollection<ParcelTripDisplaySnapshotUpdate> updates,
        CancellationToken ct = default)
        => throw new NotSupportedException("Parcel trip display snapshot backfill is not implemented by this repository.");

    IAsyncEnumerable<ParcelOperatorReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator Parcel report is not implemented by this repository.");
    Task<IReadOnlyList<PlatformParcelReportItem>> GetPlatformParcelMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
        => throw new NotSupportedException("Platform Parcel report is not implemented by this repository.");

    Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelEntity>> ListByIdsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    // Payment deposit transitions (PENDING_PAYMENT)
    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositSucceededAsync(
        Guid parcelId, Guid paymentId, long depositAmount, DateTimeOffset now, CancellationToken ct);

    Task<bool> TryAssignDepositPaymentIdAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryActivateZeroDepositAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReconcileExpiredDepositAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        bool canStillServe,
        Money refundDue,
        string cancellationReason,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> GetPaymentTransitionSnapshotAsync(
        Guid parcelId, CancellationToken ct);

    Task<bool> TrySetPendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType actionType,
        string reason,
        Money? refundAmount,
        DateTimeOffset now,
        CancellationToken ct,
        ParcelStatus? resumeStatus = null);

    Task<ParcelPaymentTransitionSnapshot?> TryResolvePendingOperatorActionAsync(
        Guid parcelId,
        PendingActionType expectedActionType,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositFailedAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkDepositExpiredAsync(
        Guid parcelId, Guid paymentId, DateTimeOffset now, CancellationToken ct);

    Task<bool> ShouldRetainDepositCargoHoldAsync(Guid parcelId, CancellationToken ct)
        => Task.FromResult(false);

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

    Task<ParcelPaymentTransitionSnapshot?> TryCheckInAsync(
        Guid parcelId,
        Guid tripId,
        string parcelCode,
        Guid checkedInByUserId,
        IReadOnlyCollection<string>? checkInPhotoUrls,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TrySettleReweighAsync(
        Guid parcelId,
        Guid reweighedByUserId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        decimal actualVolumeM3,
        decimal actualDimWeightKg,
        decimal actualChargeableWeightKg,
        ParcelSizeCategory actualSizeCategory,
        Money finalGrossPrice,
        Money finalTotalPrice,
        Money balanceRequired,
        Money refundDue,
        DateTimeOffset? finalPaymentDeadline,
        ParcelStatus resumeStatus,
        bool capacityAccepted,
        string? capacityReason,
        DateTimeOffset now,
        CancellationToken ct);

    Task<bool> TryAssignBalancePaymentIdAsync(
        Guid parcelId,
        Guid paymentId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryMarkBalanceSucceededAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        DateTimeOffset paidAt,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReconcileTimedOutBalanceAsync(
        Guid parcelId,
        Guid paymentId,
        long amount,
        DateTimeOffset paidAt,
        bool canStillServe,
        Money refundDue,
        string cancellationReason,
        DateTimeOffset now,
        CancellationToken ct);

    Task<bool> TryRecordRefundedAmountAsync(
        Guid parcelId,
        Money expectedCurrentAmount,
        Money newRefundedAmount,
        DateTimeOffset now,
        CancellationToken ct);

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

    Task<IReadOnlyList<Guid>> ListCheckInTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListFinalPaymentTimedOutIdsAsync(
        DateTimeOffset now, int maxBatch, CancellationToken ct);

    Task<IReadOnlyList<PendingParcelTripRef>> ListPendingForLoadCheckAsync(
        int maxBatch, CancellationToken ct);

    // ---- Hangfire job atomic transitions ----

    /// <summary>
    /// Auto-reject review (no human reviewer). Guards Status==PENDING_OPERATOR_REVIEW &amp;&amp; ReviewDecision==PENDING.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryAutoRejectReviewAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryRejectCheckInTimedOutAsync(
        Guid parcelId, string reason, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryRejectFinalPaymentTimedOutAsync(
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

    Task<IReadOnlyList<ParcelDeliveryReminderSnapshot>> TryBulkClaimDeliveryConfirmationRemindersAsync(
        DateTimeOffset expiredAtCutoff,
        DateTimeOffset reminderCutoff,
        DateTimeOffset now,
        int maxBatch,
        CancellationToken ct);

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
    /// Atomic: READY_TO_LOAD -> LOADED. Guards status, TripId, and ParcelCode.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkLoadedAsync(
        Guid parcelId, Guid tripId, string parcelCode, Guid? loadedByUserId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: IN_TRANSIT -> UNLOADED.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkUnloadedAsync(
        Guid parcelId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: UNLOADED -> DELIVERED_PENDING_CONFIRM.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryMarkDeliveredPendingConfirmAsync(
        Guid parcelId,
        IReadOnlyCollection<string>? deliveryPhotoUrls,
        DateTimeOffset now,
        CancellationToken ct);

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
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken ct);

    // ---- Phase 8: Operational recovery ----

    Task<ParcelPaymentTransitionSnapshot?> TryRequestTransferAsync(
        Guid parcelId, Guid operatorId, Guid targetTripId, DateTimeOffset now, CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryRequestReliabilityForwardingAsync(
        Guid parcelId,
        Guid operatorId,
        Guid targetTripId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelTransferConfirmationSnapshot?> GetTransferConfirmationSnapshotAsync(
        Guid parcelId,
        CancellationToken ct);

    Task<ParcelTransferConfirmationSnapshot?> TryClaimTransferConfirmationAsync(
        Guid parcelId,
        string parcelCode,
        Guid sourceTripId,
        Guid targetTripId,
        Guid claimId,
        Guid claimedByUserId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelTransferConfirmationSnapshot?> TryCompleteTransferConfirmationAsync(
        Guid parcelId,
        Guid sourceTripId,
        Guid targetTripId,
        Guid claimId,
        Guid confirmedByUserId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<bool> TryClearTransferConfirmationClaimAsync(
        Guid parcelId,
        Guid claimId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<IReadOnlyList<ParcelTransferConfirmationSnapshot>> GetStaleTransferConfirmationClaimsAsync(
        DateTimeOffset claimedAtCutoff,
        int maxBatch,
        CancellationToken ct);

    Task<ParcelCargoRecoveryOperationSnapshot?> GetCargoRecoveryOperationAsync(
        Guid operationId,
        CancellationToken ct);

    Task<ParcelCargoRecoveryOperationSnapshot?> GetActiveCargoRecoveryOperationAsync(
        Guid parcelId,
        CancellationToken ct);

    Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryTransferAsync(
        Guid operationId,
        Guid parcelId,
        Guid operatorId,
        Guid targetTripId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryReturnAsync(
        Guid operationId,
        Guid parcelId,
        Guid operatorId,
        Guid actorUserId,
        string reason,
        bool isStatusOverride,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelCargoRecoveryOperationSnapshot?> TryClaimCargoRecoveryReleaseAsync(
        Guid operationId,
        Guid parcelId,
        Guid sourceTripId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
        => Task.FromResult<ParcelCargoRecoveryOperationSnapshot?>(null);

    Task<ParcelPaymentTransitionSnapshot?> TryCompleteCargoRecoveryTransferAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryCompleteCargoRecoveryReturnAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<bool> TryCompleteCargoRecoveryReleaseAsync(
        Guid operationId,
        DateTimeOffset now,
        CancellationToken ct)
        => Task.FromResult(false);

    Task<bool> TryFailCargoRecoveryOperationAsync(
        Guid operationId,
        string failureCode,
        DateTimeOffset now,
        CancellationToken ct);

    Task<IReadOnlyList<ParcelCargoRecoveryOperationSnapshot>>
        GetStaleCargoRecoveryOperationsAsync(
            DateTimeOffset claimedAtCutoff,
            int maxBatch,
            CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryReturnAsync(
        Guid parcelId,
        Guid operatorId,
        Guid returnedByUserId,
        string reason,
        long refundDueVnd,
        DateTimeOffset now,
        CancellationToken ct);

    Task<IReadOnlyList<TripCancellationParcelCandidate>> GetTripCancellationCandidatesAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct);

    Task<bool> TryApplyTripCancellationAsync(
        Guid parcelId,
        Guid operatorId,
        ParcelStatus expectedStatus,
        ParcelStatus targetStatus,
        long refundDueVnd,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryCompleteRecoveryTransferAsync(
        Guid parcelId,
        Guid operatorId,
        Guid sourceTripId,
        Guid targetTripId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ParcelPaymentTransitionSnapshot?> TryManualCancelAsync(
        Guid parcelId,
        Guid operatorId,
        ParcelStatus targetStatus,
        string reason,
        long refundDueVnd,
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

    Task<PagedResult<ParcelEntity>> ListByTripAndOperatorFilteredAsync(
        Guid tripId,
        Guid operatorId,
        Guid? stopId,
        ParcelStatus? status,
        bool? hasException,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<AssistantParcelManifestCounts> GetAssistantManifestCountsAsync(
        Guid tripId,
        Guid operatorId,
        Guid? currentStopId,
        CancellationToken ct);

    Task<IReadOnlyList<ParcelEntity>> ListPendingDropoffByTripAndStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelEntity>> ListPendingTerminalDropoffByTripAsync(
        Guid tripId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelEntity>> ListDropoffManifestByTripAndStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default);

    Task<PagedResult<ParcelEntity>> ListByOperatorAsync(
        Guid operatorId,
        ParcelStatus? status,
        Guid? tripId,
        PendingActionType? pendingActionType,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<ParcelEntity>> ListByOperatorFilteredAsync(
        Guid operatorId,
        ParcelStatus? status,
        Guid? tripId,
        PendingActionType? pendingActionType,
        string? search,
        IReadOnlyCollection<Guid> senderUserIds,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string dateField,
        ParcelSizeCategory? sizeCategory,
        Guid? routeId,
        string sortBy,
        string sortDir,
        int page,
        int pageSize,
        CancellationToken ct)
        => ListByOperatorAsync(operatorId, status, tripId, pendingActionType, page, pageSize, ct);

    // ---- Phase 7: Delivery Token ----

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_CONFIRMED.
    /// Guards status and an active, unexpired token-history row.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryConfirmDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, string ip, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_REJECTED.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryRejectDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, string reason, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERY_REJECTED -> DELIVERED_PENDING_CONFIRM within undo window.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryUndoRejectDeliveryAsync(
        Guid parcelId, Guid deliveryTokenId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomically keeps pending confirmation pending, or restores a delivery rejection
    /// during its undo window, before a resend rotates the active token.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryPrepareDeliveryResendAsync(
        Guid parcelId,
        ParcelStatus expectedStatus,
        Guid expectedActiveTokenId,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Atomic: DELIVERED_PENDING_CONFIRM -> DELIVERY_CONFIRMED by operator/assistant.
    /// </summary>
    Task<ParcelPaymentTransitionSnapshot?> TryManualConfirmDeliveryAsync(
        Guid parcelId, Guid operatorId, Guid actorUserId, string note, DateTimeOffset now, CancellationToken ct);

    Task<ParcelManualConfirmationSnapshot?> GetManualConfirmationSnapshotAsync(
        Guid parcelId,
        CancellationToken ct);
}
