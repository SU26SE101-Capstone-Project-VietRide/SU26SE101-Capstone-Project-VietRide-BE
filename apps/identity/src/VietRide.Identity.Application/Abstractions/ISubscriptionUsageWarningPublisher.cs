using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Abstractions;

public interface ISubscriptionUsageWarningPublisher
{
    Task EnqueueIfThresholdCrossedAsync(
        OperatorSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionUsageResource resource,
        int delta,
        string? periodKey,
        CancellationToken cancellationToken);
}
