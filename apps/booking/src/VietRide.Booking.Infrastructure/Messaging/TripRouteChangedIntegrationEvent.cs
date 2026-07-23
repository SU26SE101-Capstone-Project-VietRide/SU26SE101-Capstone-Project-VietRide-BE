using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripRouteChangedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.route_changed";

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid TripId { get; init; }
    public Guid OperatorId { get; init; }
    public string TripStatus { get; init; } = string.Empty;
    public Guid AlternativeRouteId { get; init; }
    public IReadOnlyList<TripRouteChangedAffectedBooking> AffectedBookings { get; init; } = [];

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
