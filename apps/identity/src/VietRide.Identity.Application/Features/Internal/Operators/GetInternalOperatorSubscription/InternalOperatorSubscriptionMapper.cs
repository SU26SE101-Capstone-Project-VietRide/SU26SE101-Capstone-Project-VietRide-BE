using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

internal static class InternalOperatorSubscriptionMapper
{
    public static InternalOperatorSubscriptionDto ToDto(OperatorSubscription subscription, SubscriptionPlan plan)
    {
        return new InternalOperatorSubscriptionDto(
            subscription.OperatorId,
            subscription.Id,
            subscription.Status.ToString(),
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
            subscription.LastResetAt);
    }
}
