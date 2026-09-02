using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

public sealed record SubscriptionCustomRequestApprovedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("approvedPlanId")] Guid ApprovedPlanId,
    [property: JsonPropertyName("planName")] string PlanName)
{
    public const string EventType = "identity.subscription_custom_request.approved";
}
