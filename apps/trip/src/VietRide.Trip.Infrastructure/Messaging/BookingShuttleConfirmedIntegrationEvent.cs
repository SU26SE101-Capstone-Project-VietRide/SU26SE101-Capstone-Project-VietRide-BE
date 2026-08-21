using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Infrastructure.Messaging;

public sealed record BookingShuttleConfirmedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.confirmed";

    public Guid BookingId { get; init; }
    public string? BookingCode { get; init; }
    public Guid TripId { get; init; }
    public Guid UserId { get; init; }
    public IReadOnlyList<ConfirmedTicket> Tickets { get; init; } = [];
    public ShuttlePickupPayload? ShuttlePickup { get; init; }
    public IReadOnlyList<ShuttleRequestPayload>? ShuttleRequests { get; init; }

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;

    public sealed record ConfirmedTicket(Guid TicketId, Guid? PassengerUserId);
    public sealed record ShuttlePickupPayload(string Address, decimal Latitude, decimal Longitude);
    public sealed record ShuttleRequestPayload(
        string Direction,
        string Address,
        decimal Latitude,
        decimal Longitude,
        int? RoadDistanceMeters = null);
}
