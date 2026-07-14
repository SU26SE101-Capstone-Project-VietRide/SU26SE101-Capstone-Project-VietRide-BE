using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record PaymentFailedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "payment.payment.failed";
    public Guid PaymentId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public string Reason { get; init; } = string.Empty;
    [JsonIgnore] public Guid EventId => PaymentId;
    [JsonIgnore] public DateTime OccurredAt => DateTime.UtcNow;
    [JsonIgnore] string IIntegrationEvent.EventType => EventType;
}
