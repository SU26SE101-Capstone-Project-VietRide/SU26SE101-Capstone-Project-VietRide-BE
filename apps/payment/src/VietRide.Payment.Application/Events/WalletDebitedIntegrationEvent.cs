using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class WalletDebitedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "payment.wallet.debited";

    public WalletDebitedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid userId,
        Guid walletTransactionId,
        long amount,
        long balanceAfter,
        string referenceType,
        Guid referenceId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        UserId = userId;
        WalletTransactionId = walletTransactionId;
        Amount = amount;
        BalanceAfter = balanceAfter;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
    }

    [JsonPropertyName("userId")]
    public Guid UserId { get; }

    [JsonPropertyName("walletTransactionId")]
    public Guid WalletTransactionId { get; }

    [JsonPropertyName("amount")]
    public long Amount { get; }

    [JsonPropertyName("balanceAfter")]
    public long BalanceAfter { get; }

    [JsonPropertyName("referenceType")]
    public string ReferenceType { get; }

    [JsonPropertyName("referenceId")]
    public Guid ReferenceId { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
