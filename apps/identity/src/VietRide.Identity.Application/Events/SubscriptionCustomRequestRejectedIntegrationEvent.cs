using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

public sealed record SubscriptionCustomRequestRejectedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("rejectionReason")] string RejectionReason)
{
    public const string EventType = "identity.subscription_custom_request.rejected";
}
