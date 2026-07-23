using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripRouteChangedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.route_changed";

    public TripRouteChangedIntegrationEvent(
        Guid tripId,
        Guid operatorId,
        Guid alternativeRouteId,
        IReadOnlyList<Guid> affectedBookingIds,
        DateTimeOffset? occurredAt = null)
        : this(Guid.NewGuid(), occurredAt ?? DateTimeOffset.UtcNow, tripId, operatorId, alternativeRouteId, affectedBookingIds)
    {
    }

    public TripRouteChangedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        Guid alternativeRouteId,
        IReadOnlyList<Guid> affectedBookingIds)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        OperatorId = operatorId;
        AlternativeRouteId = alternativeRouteId;
        AffectedBookingIds = affectedBookingIds?.Distinct().OrderBy(id => id).ToArray()
            ?? throw new ArgumentNullException(nameof(affectedBookingIds));
    }

    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public Guid AlternativeRouteId { get; }
    public IReadOnlyList<Guid> AffectedBookingIds { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
