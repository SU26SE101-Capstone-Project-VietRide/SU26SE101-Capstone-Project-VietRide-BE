using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Application.Events;

public sealed class OperatorRegistrationSubmittedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "identity.operator.registration_submitted";

    public OperatorRegistrationSubmittedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid operatorId,
        string companyName)
        : base(eventId, occurredAt.UtcDateTime)
    {
        OperatorId = operatorId;
        CompanyName = companyName;
    }

    [JsonPropertyName("operatorId")]
    public Guid OperatorId { get; }

    [JsonPropertyName("companyName")]
    public string CompanyName { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
