using System.Text.Json;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels;

public static class ParcelOutboxEvents
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    public const string Created = "parcel.parcel.created";
    public const string Reserved = "parcel.parcel.reserved";
    public const string Loaded = "parcel.parcel.loaded";
    public const string Unloaded = "parcel.parcel.unloaded";
    public const string DeliveredPendingConfirm = "parcel.parcel.delivered_pending_confirm";
    public const string DeliveryConfirmed = "parcel.parcel.delivery_confirmed";
    public const string DeliveryRejected = "parcel.parcel.delivery_rejected";
    public const string DeliveryRejectUndone = "parcel.parcel.delivery_reject_undone";
    public const string StatusOverridden = "parcel.parcel.status_overridden";
    public const string Cancelled = "parcel.parcel.cancelled";
    public const string Rejected = "parcel.parcel.rejected";
    public const string Returned = "parcel.parcel.returned";
    public const string AutoRejected = "parcel.parcel.auto_rejected";
    public const string ReviewRequested = "parcel.parcel.review_requested";
    public const string ReviewApproved = "parcel.parcel.review_approved";
    public const string FinalPaymentRequested = "parcel.parcel.final_payment_requested";
    public const string SettlementRecovered = "parcel.parcel.settlement_recovered";
    public const string TransferInitiated = "parcel.parcel.transfer_initiated";
    public const string TransferConfirmed = "parcel.parcel.transfer_confirmed";
    public const string TransferEscalated = "parcel.parcel.transfer_escalated";
    public const string ReturnInitiated = "parcel.parcel.return_initiated";
    public const string PendingOperatorAction = "parcel.parcel.pending_operator_action";
    public const string PendingOperatorActionRealerted = "parcel.parcel.pending_operator_action_realerted";
    public const string DeliveryConfirmationRealerted = "parcel.parcel.delivery_confirmation_realerted";
    public const string RefundInitiated = "parcel.refund.initiated";
    public const string CustodyEventRecorded = "parcel.custody.event_recorded";
    public const string IncidentOpened = "parcel.incident.opened";
    public const string IncidentUpdated = "parcel.incident.updated";
    public const string ApprovalRequested = "parcel.approval.requested";
    public const string ParcelClaimSubmitted = "parcel.claim.submitted";
    public const string ParcelClaimDecided = "parcel.claim.decided";
    public const string ParcelClaimAppealed = "parcel.claim.appealed";
    public const string ParcelClaimAppealDecided = "parcel.claim_appeal.decided";

    public static Task EnqueueAsync(
        IIntegrationEventOutbox outbox,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
        => outbox.EnqueueAsync(eventType, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);

    public static Task EnqueueAsync(
        IIntegrationEventOutbox outbox,
        Guid eventId,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
        => outbox.EnqueueAsync(
            eventId,
            eventType,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);

    public static Task EnqueueRefundAsync(
        IIntegrationEventOutbox outbox,
        Guid parcelId,
        Guid senderUserId,
        long amount,
        CancellationToken cancellationToken)
        => EnqueueRefundAsync(
            outbox,
            parcelId,
            senderUserId,
            amount,
            parcelId.ToString("D"),
            cancellationToken);

    public static Task EnqueueRefundAsync(
        IIntegrationEventOutbox outbox,
        Guid parcelId,
        Guid senderUserId,
        long amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
        => EnqueueAsync(
            outbox,
            RefundInitiated,
            new
            {
                parcelId,
                senderUserId,
                amount,
                referenceType = "PARCEL_REFUND",
                referenceId = parcelId,
                idempotencyKey,
            },
            cancellationToken);

    public static Task EnqueueTerminalAsync(
        IIntegrationEventOutbox outbox,
        Guid eventId,
        DateTimeOffset occurredAt,
        string eventType,
        Guid parcelId,
        string parcelCode,
        Guid operatorId,
        Guid senderUserId,
        Guid tripId,
        long refundAmount,
        string reason,
        CancellationToken cancellationToken)
        => EnqueueAsync(
            outbox,
            eventId,
            eventType,
            new
            {
                eventId,
                occurredAt,
                parcelId,
                parcelCode,
                operatorId,
                userId = senderUserId,
                tripId,
                refundAmount,
                reason,
            },
            cancellationToken);

    public static Task EnqueueCanonicalRefundAsync(
        IIntegrationEventOutbox outbox,
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid parcelId,
        Guid senderUserId,
        long amount,
        string reason,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
        => EnqueueAsync(
            outbox,
            eventId,
            RefundInitiated,
            new
            {
                eventId,
                occurredAt,
                parcelId,
                senderUserId,
                amount,
                referenceType = "PARCEL_REFUND",
                referenceId = parcelId,
                reason,
                idempotencyKey,
            },
            cancellationToken);
}
