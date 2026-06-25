using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record WalletCreditedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "payment.wallet.credited";

    public Guid UserId { get; init; }
    public long Amount { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }

    [JsonIgnore]
    public Guid EventId => ReferenceId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
