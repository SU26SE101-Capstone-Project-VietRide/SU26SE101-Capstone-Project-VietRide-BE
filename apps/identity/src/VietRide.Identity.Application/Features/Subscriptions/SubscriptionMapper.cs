using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions;

internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToPlanDto(SubscriptionPlan plan) => new(
        plan.Id,
        plan.Name,
        plan.Description,
        plan.PricePerMonth.Amount,
        plan.PricePerYear.Amount,
        new SubscriptionLimitsDto(
            plan.MaxVehicles,
            plan.MaxDrivers,
            plan.MaxAssistants,
            plan.MaxOperatorUsers,
            plan.MaxRoutes,
            plan.MaxTripsPerMonth),
        new SubscriptionModulesDto(plan.EnableParcel, plan.EnableShuttle, plan.EnableRag),
        plan.IsActive,
        plan.PlanType.ToString(),
        plan.OwnerOperatorId);

    public static OperatorSubscriptionDto ToSubscriptionDto(
        OperatorSubscription subscription,
        SubscriptionPlan plan,
        SubscriptionUpgradeAttempt? pendingUpgrade,
        SubscriptionPlan? targetPlan,
        DateTimeOffset now) => new(
        subscription.Id,
        SubscriptionEffectiveState.GetStatus(subscription, now).ToString(),
        subscription.BillingPeriod?.ToString(),
        subscription.StartedAt,
        subscription.ExpiresAt,
        ToPlanDto(plan),
        new SubscriptionUsageDto(
            subscription.CurrentVehicles,
            subscription.CurrentDrivers,
            subscription.CurrentAssistants,
            subscription.CurrentOperatorUsers,
            subscription.CurrentRoutes,
            subscription.CurrentTripsThisMonth,
            subscription.LastResetAt),
        pendingUpgrade is null ? null : new PendingSubscriptionUpgradeDto(
            pendingUpgrade.Id,
            pendingUpgrade.Status.ToString(),
            ToPlanDto(targetPlan ?? throw new InvalidOperationException("Pending upgrade target plan is missing.")),
            pendingUpgrade.BillingPeriod.ToString(),
            pendingUpgrade.Amount.Amount,
            pendingUpgrade.DueAt,
            Math.Max(0, (int)Math.Ceiling((pendingUpgrade.DueAt - now).TotalSeconds)),
            new PendingSubscriptionPaymentDto(
                pendingUpgrade.PaymentId,
                pendingUpgrade.LatestPaymentStatus.ToString(),
                pendingUpgrade.DueAt > now
                    && pendingUpgrade.LatestPaymentStatus is SubscriptionPaymentSessionStatus.FAILED
                        or SubscriptionPaymentSessionStatus.EXPIRED)),
        SubscriptionEffectiveState.IsEntitlementActive(subscription, now));

    public static SubscriptionBillingPeriod ParseBillingPeriod(string value)
        => Enum.TryParse<SubscriptionBillingPeriod>(value, ignoreCase: false, out var period)
            ? period
            : throw new ArgumentOutOfRangeException(nameof(value), "Billing period must be MONTHLY or YEARLY.");
}
