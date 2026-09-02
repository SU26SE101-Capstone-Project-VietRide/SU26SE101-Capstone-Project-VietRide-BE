using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

public sealed record SubscriptionCustomRequestSubmittedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("operatorName")] string OperatorName)
{
    public const string EventType = "identity.subscription_custom_request.submitted";
}
