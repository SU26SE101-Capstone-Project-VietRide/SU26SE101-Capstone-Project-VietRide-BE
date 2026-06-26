using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record BookingConfirmedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.confirmed";

    public Guid BookingId { get; init; }
    public Guid TripId { get; init; }
    public long TotalAmount { get; init; }
    public Guid UserId { get; init; }
    public Guid? VoucherUsageId { get; init; }

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
