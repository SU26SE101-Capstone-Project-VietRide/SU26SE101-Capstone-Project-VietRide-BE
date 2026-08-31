using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Services;

internal static class ParcelApprovalRequestedEvent
{
    public static Task EnqueueAsync(
        IIntegrationEventOutbox outbox,
        Guid approvalRequestId,
        string requestType,
        Guid operatorId,
        Guid targetDriverUserId,
        Guid tripId,
        Guid? parcelId,
        Guid? incidentId,
        Guid? stopId,
        string validityCondition,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        return ParcelOutboxEvents.EnqueueAsync(
            outbox,
            eventId,
            ParcelOutboxEvents.ApprovalRequested,
            new
            {
                eventId,
                occurredAt,
                approvalRequestId,
                requestType,
                operatorId,
                targetDriverUserId,
                tripId,
                parcelId,
                incidentId,
                stopId,
                expiresAt = (DateTimeOffset?)null,
                validityCondition,
                actionType = "OPEN_PARCEL_APPROVAL",
                actionParams = new { requestId = approvalRequestId, requestType },
            },
            cancellationToken);
    }
}
