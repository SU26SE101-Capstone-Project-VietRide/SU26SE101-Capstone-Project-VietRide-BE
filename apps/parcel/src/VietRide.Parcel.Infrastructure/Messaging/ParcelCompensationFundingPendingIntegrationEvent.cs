using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record ParcelCompensationFundingPendingIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "payment.parcel_compensation.funding_pending";
    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid PayoutId { get; init; }
    public Guid ClaimId { get; init; }
    [JsonIgnore]
    public string EventType => EventTypeValue;
}
