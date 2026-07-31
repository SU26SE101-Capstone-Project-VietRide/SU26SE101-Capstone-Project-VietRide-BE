using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record IdentityUserDeletedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "identity.user.deleted";

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }

    [JsonRequired]
    public Guid UserId { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
