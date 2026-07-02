using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class ParcelRefundInitiatedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "parcel.refund.initiated";

    [JsonPropertyName("parcelId")]
    public Guid ParcelId { get; init; }

    [JsonPropertyName("senderUserId")]
    public Guid SenderUserId { get; init; }

    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("referenceType")]
    public string ReferenceType { get; init; } = string.Empty;

    [JsonPropertyName("referenceId")]
    public Guid ReferenceId { get; init; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }

    public override string EventType => EventTypeValue;
}
