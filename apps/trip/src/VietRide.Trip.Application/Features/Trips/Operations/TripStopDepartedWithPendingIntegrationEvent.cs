using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripStopDepartedWithPendingIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.stop.departed_with_pending";

    public TripStopDepartedWithPendingIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid stopId,
        string stopName,
        int pendingPassengerCount,
        Guid driverUserId,
        Guid? assistantUserId,
        DateTimeOffset departedAt)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        StopId = stopId;
        StopName = stopName;
        PendingPassengerCount = pendingPassengerCount;
        DriverUserId = driverUserId;
        AssistantUserId = assistantUserId;
        DepartedAt = departedAt.ToUniversalTime();
    }

    public override string EventType => EventTypeValue;

    public Guid TripId { get; }
    public Guid StopId { get; }
    public string StopName { get; }
    public int PendingPassengerCount { get; }
    public Guid DriverUserId { get; }
    public Guid? AssistantUserId { get; }
    public DateTimeOffset DepartedAt { get; }
}
