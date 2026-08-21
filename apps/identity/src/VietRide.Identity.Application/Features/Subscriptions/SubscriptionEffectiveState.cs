using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions;

public static class SubscriptionEffectiveState
{
    public static SubscriptionStatus GetStatus(OperatorSubscription subscription, DateTimeOffset decisionAt)
    {
        if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.PENDING_PAYMENT
            && subscription.ExpiresAt.HasValue
            && subscription.ExpiresAt.Value <= decisionAt)
        {
            return SubscriptionStatus.EXPIRED;
        }

        return subscription.Status;
    }

    public static bool IsEntitlementActive(OperatorSubscription subscription, DateTimeOffset decisionAt)
        => subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.PENDING_PAYMENT
            && subscription.ExpiresAt.HasValue
            && subscription.ExpiresAt.Value > decisionAt;
}
