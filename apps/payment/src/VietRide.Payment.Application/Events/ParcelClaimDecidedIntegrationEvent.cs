using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class ParcelClaimDecidedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "parcel.claim.decided";

    [JsonPropertyName("claimId")]
    public Guid ClaimId { get; init; }

    [JsonPropertyName("parcelId")]
    public Guid ParcelId { get; init; }

    [JsonPropertyName("tripId")]
    public Guid? TripId { get; init; }

    [JsonPropertyName("operatorId")]
    public Guid OperatorId { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("totalAwardVnd")]
    public long TotalAwardVnd { get; init; }

    [JsonPropertyName("beneficiaryUserId")]
    public Guid BeneficiaryUserId { get; init; }

    public override string EventType => EventTypeValue;
}
