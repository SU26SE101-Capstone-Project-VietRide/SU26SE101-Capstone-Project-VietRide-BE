using VietRide.Shared.Messaging.Abstractions;
using VietRide.Trip.Application.Features.Stations.MergeStations;

namespace VietRide.Trip.Application.Events;

public sealed class StationMergedIntegrationEvent : IntegrationEventBase
{
    public StationMergedIntegrationEvent(
        Guid actorUserId,
        string? ipAddress,
        string? userAgent,
        Guid primaryStationId,
        Guid duplicateStationId,
        StationEventSnapshot primaryBefore,
        StationEventSnapshot duplicateBefore,
        StationEventSnapshot primaryAfter,
        StationRelinkedCounts relinkedCounts,
        DateTimeOffset occurredAt)
        : base(Guid.NewGuid(), occurredAt.UtcDateTime)
    {
        ActorUserId = actorUserId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        PrimaryStationId = primaryStationId;
        DuplicateStationId = duplicateStationId;
        PrimaryBefore = primaryBefore;
        DuplicateBefore = duplicateBefore;
        PrimaryAfter = primaryAfter;
        RelinkedCounts = relinkedCounts;
    }

    public override string EventType => "trip.station.merged";

    public Guid ActorUserId { get; }
    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public Guid PrimaryStationId { get; }
    public Guid DuplicateStationId { get; }
    public StationEventSnapshot PrimaryBefore { get; }
    public StationEventSnapshot DuplicateBefore { get; }
    public StationEventSnapshot PrimaryAfter { get; }
    public StationRelinkedCounts RelinkedCounts { get; }
}
