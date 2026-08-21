using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

internal static class InternalOperatorSubscriptionMapper
{
    public static InternalOperatorSubscriptionDto ToDto(
        OperatorSubscription subscription,
        SubscriptionPlan plan,
        DateTimeOffset decisionAt)
    {
        return new InternalOperatorSubscriptionDto(
            subscription.OperatorId,
            subscription.Id,
            SubscriptionEffectiveState.GetStatus(subscription, decisionAt).ToString(),
            subscription.StartedAt,
            subscription.ExpiresAt,
            new InternalSubscriptionPlanDto(
                plan.Id,
                plan.Name,
                new InternalSubscriptionLimitsDto(
                    plan.MaxVehicles,
                    plan.MaxDrivers,
                    plan.MaxAssistants,
                    plan.MaxOperatorUsers,
                    plan.MaxRoutes,
                    plan.MaxTripsPerMonth),
                new InternalSubscriptionModulesDto(
                    plan.EnableParcel,
                    plan.EnableShuttle,
                    plan.EnableRag)),
            new InternalSubscriptionUsageDto(
                subscription.CurrentVehicles,
                subscription.CurrentDrivers,
                subscription.CurrentAssistants,
                subscription.CurrentOperatorUsers,
                subscription.CurrentRoutes,
                subscription.CurrentTripsThisMonth),
            subscription.LastResetAt,
            SubscriptionEffectiveState.IsEntitlementActive(subscription, decisionAt));
    }
}
