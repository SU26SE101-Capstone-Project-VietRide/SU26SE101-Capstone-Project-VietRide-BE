using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class SubscriptionQuotaAllocation : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public SubscriptionUsageResource Resource { get; private set; }
    public Guid ResourceId { get; private set; }
    public string? PeriodKey { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    private SubscriptionQuotaAllocation() { }

    public static SubscriptionQuotaAllocation Create(
        Guid operatorId,
        Guid subscriptionId,
        SubscriptionUsageResource resource,
        Guid resourceId,
        string? periodKey)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("Resource id is required.", nameof(resourceId));
        if (resource == SubscriptionUsageResource.TRIPS_THIS_MONTH && !IsPeriodKey(periodKey))
            throw new ArgumentException("Trips allocation requires periodKey in yyyy-MM format.", nameof(periodKey));
        if (resource != SubscriptionUsageResource.TRIPS_THIS_MONTH && periodKey is not null)
            throw new ArgumentException("Only trips allocation may include periodKey.", nameof(periodKey));

        return new SubscriptionQuotaAllocation
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            SubscriptionId = subscriptionId,
            Resource = resource,
            ResourceId = resourceId,
            PeriodKey = periodKey,
        };
    }

    public void Release(DateTimeOffset releasedAt)
    {
        ReleasedAt ??= releasedAt;
    }

    private static bool IsPeriodKey(string? value)
        => value is { Length: 7 }
            && DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", out _);
}
