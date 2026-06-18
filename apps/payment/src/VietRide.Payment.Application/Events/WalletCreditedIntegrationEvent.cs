using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

/// <summary>
/// Emitted when a passenger wallet top-up is credited successfully.
/// Canonical routing key: payment.wallet.credited.
/// </summary>
public sealed class WalletCreditedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "payment.wallet.credited";

    public WalletCreditedIntegrationEvent(
        Guid userId,
        long amount,
        string referenceType,
        Guid referenceId)
    {
        UserId = userId;
        Amount = amount;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
    }

    [JsonPropertyName("userId")]
    public Guid UserId { get; }

    [JsonPropertyName("amount")]
    public long Amount { get; }

    [JsonPropertyName("referenceType")]
    public string ReferenceType { get; }

    [JsonPropertyName("referenceId")]
    public Guid ReferenceId { get; }

    public override string EventType => EventTypeValue;
}
