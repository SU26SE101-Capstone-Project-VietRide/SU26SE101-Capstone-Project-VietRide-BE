using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class StationNormalizedIntegrationEvent : IntegrationEventBase
{
    public StationNormalizedIntegrationEvent(
        Guid actorUserId,
        string? ipAddress,
        string? userAgent,
        Guid stationId,
        StationEventSnapshot before,
        StationEventSnapshot after,
        DateTimeOffset occurredAt)
        : base(Guid.NewGuid(), occurredAt.UtcDateTime)
    {
        ActorUserId = actorUserId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        StationId = stationId;
        Before = before;
        After = after;
    }

    public override string EventType => "trip.station.normalized";

    public Guid ActorUserId { get; }
    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public Guid StationId { get; }
    public StationEventSnapshot Before { get; }
    public StationEventSnapshot After { get; }
}
