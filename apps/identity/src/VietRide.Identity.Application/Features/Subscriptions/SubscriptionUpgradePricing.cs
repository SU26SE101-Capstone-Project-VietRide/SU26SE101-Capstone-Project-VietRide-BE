using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Subscriptions;

public sealed record SubscriptionUpgradePrice(
    bool IsProrated,
    Money CurrentCyclePrice,
    Money TargetCyclePrice,
    Money UnusedCredit,
    Money ProratedTargetAmount,
    Money AmountDue,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    DateTimeOffset DueAt);

public static class SubscriptionUpgradePricing
{
    private static readonly TimeSpan QuoteWindow = TimeSpan.FromMinutes(15);

    public static SubscriptionUpgradePrice Calculate(
        OperatorSubscription subscription,
        SubscriptionPlan targetPlan,
        SubscriptionBillingPeriod requestedBillingPeriod,
        DateTimeOffset decisionAt)
    {
        var targetCyclePrice = requestedBillingPeriod == SubscriptionBillingPeriod.MONTHLY
            ? targetPlan.PricePerMonth
            : targetPlan.PricePerYear;
        if (targetCyclePrice.Amount <= 0)
            throw InvalidAmount();

        var paidActive = SubscriptionEffectiveState.IsEntitlementActive(subscription, decisionAt)
            && subscription.BillingPeriod.HasValue
            && subscription.CyclePriceAmount.Amount > 0;
        if (!paidActive)
        {
            var periodTo = requestedBillingPeriod == SubscriptionBillingPeriod.MONTHLY
                ? decisionAt.AddMonths(1)
                : decisionAt.AddYears(1);
            return new SubscriptionUpgradePrice(
                false,
                Money.Zero,
                targetCyclePrice,
                Money.Zero,
                targetCyclePrice,
                targetCyclePrice,
                decisionAt,
                periodTo,
                decisionAt.Add(QuoteWindow));
        }

        if (subscription.BillingPeriod != requestedBillingPeriod)
        {
            throw new CodedValidationException(
                "SUBSCRIPTION_UPGRADE_BILLING_PERIOD_MISMATCH",
                "An active paid subscription must be upgraded within the same billing period.");
        }

        if (subscription.PlanId == targetPlan.Id || targetCyclePrice.Amount <= subscription.CyclePriceAmount.Amount)
            throw InvalidAmount();
        if (!subscription.StartedAt.HasValue
            || !subscription.ExpiresAt.HasValue
            || subscription.ExpiresAt.Value <= subscription.StartedAt.Value)
        {
            throw new CodedValidationException(
                "SUBSCRIPTION_UPGRADE_PERIOD_INVALID",
                "The current paid subscription period is invalid.");
        }

        var remainingTicks = (subscription.ExpiresAt.Value - decisionAt).Ticks;
        var totalTicks = (subscription.ExpiresAt.Value - subscription.StartedAt.Value).Ticks;
        var remainingRatio = remainingTicks / (decimal)totalTicks;
        var unusedCredit = Money.FromDecimal(subscription.CyclePriceAmount.Amount * remainingRatio);
        var proratedTarget = Money.FromDecimal(targetCyclePrice.Amount * remainingRatio);
        var amountDue = proratedTarget.Amount - unusedCredit.Amount;
        if (amountDue <= 0)
            throw InvalidAmount();

        var dueAt = decisionAt.Add(QuoteWindow);
        if (dueAt > subscription.ExpiresAt.Value)
            dueAt = subscription.ExpiresAt.Value;

        return new SubscriptionUpgradePrice(
            true,
            subscription.CyclePriceAmount,
            targetCyclePrice,
            unusedCredit,
            proratedTarget,
            Money.FromRaw(amountDue),
            decisionAt,
            subscription.ExpiresAt.Value,
            dueAt);
    }

    private static CodedValidationException InvalidAmount()
        => new(
            "SUBSCRIPTION_UPGRADE_AMOUNT_NOT_PAYABLE",
            "The selected target plan does not produce a payable upgrade amount.");
}
