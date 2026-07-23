using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripRouteChangedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.route_changed";
    private static readonly HashSet<string> AllowedTripStatuses =
        ["SCHEDULED", "BOARDING", "IN_PROGRESS"];

    public TripRouteChangedIntegrationEvent(
        Guid tripId,
        Guid operatorId,
        string tripStatus,
        Guid alternativeRouteId,
        IReadOnlyList<TripRouteChangedAffectedBooking> affectedBookings,
        DateTimeOffset? occurredAt = null)
        : this(
            Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            tripId,
            operatorId,
            tripStatus,
            alternativeRouteId,
            affectedBookings)
    {
    }

    public TripRouteChangedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        string tripStatus,
        Guid alternativeRouteId,
        IReadOnlyList<TripRouteChangedAffectedBooking> affectedBookings)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (!AllowedTripStatuses.Contains(tripStatus))
            throw new ArgumentOutOfRangeException(nameof(tripStatus), tripStatus, "Trip status does not allow route changes.");

        TripId = tripId;
        OperatorId = operatorId;
        TripStatus = tripStatus;
        AlternativeRouteId = alternativeRouteId;
        AffectedBookings = Array.AsReadOnly(
            affectedBookings?.GroupBy(booking => booking.BookingId)
                .Select(group => group.First())
                .OrderBy(booking => booking.BookingId)
                .ToArray()
                ?? throw new ArgumentNullException(nameof(affectedBookings)));
    }

    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public string TripStatus { get; }
    public Guid AlternativeRouteId { get; }
    public IReadOnlyList<TripRouteChangedAffectedBooking> AffectedBookings { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
