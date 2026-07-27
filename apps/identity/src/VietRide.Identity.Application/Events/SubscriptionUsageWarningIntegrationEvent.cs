using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Application.Events;

public sealed class SubscriptionUsageWarningIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "identity.subscription.usage_warning";

    public SubscriptionUsageWarningIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid subscriptionId,
        Guid operatorId,
        string resource,
        string periodKey,
        int used,
        int limit,
        decimal usagePercent)
        : base(eventId, occurredAt.UtcDateTime)
    {
        SubscriptionId = subscriptionId;
        OperatorId = operatorId;
        Resource = resource;
        PeriodKey = periodKey;
        Used = used;
        Limit = limit;
        UsagePercent = usagePercent;
    }

    [JsonPropertyName("subscriptionId")]
    public Guid SubscriptionId { get; }

    [JsonPropertyName("operatorId")]
    public Guid OperatorId { get; }

    [JsonPropertyName("resource")]
    public string Resource { get; }

    [JsonPropertyName("periodKey")]
    public string PeriodKey { get; }

    [JsonPropertyName("used")]
    public int Used { get; }

    [JsonPropertyName("limit")]
    public int Limit { get; }

    [JsonPropertyName("usagePercent")]
    public decimal UsagePercent { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
