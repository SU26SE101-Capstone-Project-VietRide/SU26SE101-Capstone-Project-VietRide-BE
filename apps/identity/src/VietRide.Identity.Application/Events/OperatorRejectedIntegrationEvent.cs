using System.Text.Json.Serialization;

namespace VietRide.Identity.Application.Events;

public sealed record OperatorRejectedIntegrationEvent(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("operatorId")] Guid OperatorId,
    [property: JsonPropertyName("companyName")] string CompanyName,
    [property: JsonPropertyName("contactEmail")] string ContactEmail,
    [property: JsonPropertyName("reason")] string Reason)
{
    public const string EventType = "identity.operator.rejected";
}
