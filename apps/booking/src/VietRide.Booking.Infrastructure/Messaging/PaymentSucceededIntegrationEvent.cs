using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record PaymentSucceededIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "payment.payment.succeeded";

    public Guid PaymentId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public long Amount { get; init; }
    public string Method { get; init; } = string.Empty;
    public DateTimeOffset? PaidAt { get; init; }
    public DateTimeOffset? DueAt { get; init; }

    [JsonIgnore]
    public Guid EventId => PaymentId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
