using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class StationNormalizedIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "trip.station.normalized";

    public Guid ActorUserId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public Guid StationId { get; init; }
    public StationAuditSnapshot? Before { get; init; }
    public StationAuditSnapshot? After { get; init; }
}
