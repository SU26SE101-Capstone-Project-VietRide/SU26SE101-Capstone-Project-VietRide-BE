using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class SubscriptionUsageWarningMarker : BaseEntity<Guid>
{
    private SubscriptionUsageWarningMarker() { }

    public Guid SubscriptionId { get; private set; }
    public SubscriptionUsageResource Resource { get; private set; }
    public string PeriodKey { get; private set; } = string.Empty;

    public static SubscriptionUsageWarningMarker Create(
        Guid eventId,
        Guid subscriptionId,
        SubscriptionUsageResource resource,
        string periodKey)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event id is required.", nameof(eventId));
        if (subscriptionId == Guid.Empty)
            throw new ArgumentException("Subscription id is required.", nameof(subscriptionId));
        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("Period key is required.", nameof(periodKey));

        return new SubscriptionUsageWarningMarker
        {
            Id = eventId,
            SubscriptionId = subscriptionId,
            Resource = resource,
            PeriodKey = periodKey,
        };
    }
}
