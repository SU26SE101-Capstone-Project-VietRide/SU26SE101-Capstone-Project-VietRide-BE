using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions;

public sealed record SubscriptionQuotaViolation(
    string Field,
    int GrantedLimit,
    int CurrentUsage);

public static class SubscriptionQuotaPolicy
{
    public static IReadOnlyList<SubscriptionQuotaViolation> GetLimitsBelowCurrentUsage(
        OperatorSubscription subscription,
        SubscriptionPlan targetPlan)
    {
        var violations = new List<SubscriptionQuotaViolation>(6);
        AddIfBelow(violations, "maxVehicles", targetPlan.MaxVehicles, subscription.CurrentVehicles);
        AddIfBelow(violations, "maxDrivers", targetPlan.MaxDrivers, subscription.CurrentDrivers);
        AddIfBelow(violations, "maxAssistants", targetPlan.MaxAssistants, subscription.CurrentAssistants);
        AddIfBelow(violations, "maxOperatorUsers", targetPlan.MaxOperatorUsers, subscription.CurrentOperatorUsers);
        AddIfBelow(violations, "maxRoutes", targetPlan.MaxRoutes, subscription.CurrentRoutes);
        AddIfBelow(violations, "maxTripsPerMonth", targetPlan.MaxTripsPerMonth, subscription.CurrentTripsThisMonth);
        return violations;
    }

    public static int GetLimit(SubscriptionPlan plan, SubscriptionUsageResource resource)
        => resource switch
        {
            SubscriptionUsageResource.VEHICLES => plan.MaxVehicles,
            SubscriptionUsageResource.DRIVERS => plan.MaxDrivers,
            SubscriptionUsageResource.ASSISTANTS => plan.MaxAssistants,
            SubscriptionUsageResource.OPERATOR_USERS => plan.MaxOperatorUsers,
            SubscriptionUsageResource.ROUTES => plan.MaxRoutes,
            SubscriptionUsageResource.TRIPS_THIS_MONTH => plan.MaxTripsPerMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };

    private static void AddIfBelow(
        List<SubscriptionQuotaViolation> violations,
        string field,
        int limit,
        int current)
    {
        if (limit < current)
            violations.Add(new SubscriptionQuotaViolation(field, limit, current));
    }
}
