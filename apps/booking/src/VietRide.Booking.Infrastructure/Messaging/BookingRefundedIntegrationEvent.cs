using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record BookingRefundedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.refunded";

    public Guid BookingId { get; init; }
    public Guid UserId { get; init; }
    public long Amount { get; init; }

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
