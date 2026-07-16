using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripVehicleSwappedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.vehicle_swapped";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public DateTime OccurredAt { get; init; }
    [JsonRequired]
    public Guid TripId { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public Guid OldVehicleId { get; init; }
    [JsonRequired]
    public Guid NewVehicleId { get; init; }
    [JsonRequired]
    public string OldVehiclePlateNumber { get; init; } = string.Empty;
    [JsonRequired]
    public string NewVehiclePlateNumber { get; init; } = string.Empty;
    [JsonRequired]
    public DateTimeOffset DepartureDateTime { get; init; }
    [JsonRequired]
    public Guid DriverUserId { get; init; }
    [JsonRequired]
    public Guid? AssistantUserId { get; init; }
    [JsonRequired]
    public IReadOnlyCollection<TripVehicleSwapSeatImpact> SeatImpacts { get; init; } = [];

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripVehicleSwapSeatImpact
{
    [JsonRequired]
    public Guid BookingId { get; init; }
    [JsonRequired]
    public IReadOnlyCollection<string> SeatNumbers { get; init; } = [];
    [JsonRequired]
    public string Reason { get; init; } = string.Empty;
}
