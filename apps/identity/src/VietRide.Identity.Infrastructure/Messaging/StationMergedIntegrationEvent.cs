using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class StationMergedIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "trip.station.merged";

    public Guid ActorUserId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public Guid PrimaryStationId { get; init; }
    public Guid DuplicateStationId { get; init; }
    public StationAuditSnapshot? PrimaryBefore { get; init; }
    public StationAuditSnapshot? DuplicateBefore { get; init; }
    public StationAuditSnapshot? PrimaryAfter { get; init; }
    public StationRelinkedCounts? RelinkedCounts { get; init; }
}
