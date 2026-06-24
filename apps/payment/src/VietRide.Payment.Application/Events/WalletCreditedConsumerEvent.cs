using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

/// <summary>
/// Consumer-side mirror of the payment.wallet.credited payload. Payment consumes its OWN canonical
/// wallet-credit event to drive the Payment row to REFUNDED for refund credits (BSOT §8.4). Keep the
/// JSON names in sync with <see cref="WalletCreditedIntegrationEvent"/> (the publisher).
/// </summary>
public sealed record WalletCreditedConsumerEvent(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("referenceType")] string ReferenceType,
    [property: JsonPropertyName("referenceId")] Guid ReferenceId) : IIntegrationEvent
{
    public const string EventType = WalletCreditedIntegrationEvent.EventTypeValue;

    [JsonIgnore]
    public Guid EventId => ReferenceId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
