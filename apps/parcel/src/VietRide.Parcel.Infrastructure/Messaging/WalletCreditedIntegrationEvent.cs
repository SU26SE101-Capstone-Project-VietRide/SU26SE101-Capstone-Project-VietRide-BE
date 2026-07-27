using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record WalletCreditedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "payment.wallet.credited";

    public Guid EventId { get; init; }
    public Guid UserId { get; init; }
    public long Amount { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }

    [JsonPropertyName("occurredAt")]
    public DateTime EventOccurredAt { get; init; }

    [JsonIgnore]
    public DateTime OccurredAt => EventOccurredAt;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
