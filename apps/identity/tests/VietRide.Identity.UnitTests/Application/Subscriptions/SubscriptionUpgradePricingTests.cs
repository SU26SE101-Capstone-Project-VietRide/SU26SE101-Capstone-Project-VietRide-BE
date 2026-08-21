using FluentAssertions;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Subscriptions;

public sealed class SubscriptionUpgradePricingTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Calculate_ActivePaidSubscription_ProrationPreservesDoubleRoundedInvariant()
    {
        var subscription = CreatePaidSubscription(300_000);
        var target = CreatePlan(500_000);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            StartedAt.AddDays(15));

        quote.IsProrated.Should().BeTrue();
        quote.CurrentCyclePrice.Amount.Should().Be(300_000);
        quote.TargetCyclePrice.Amount.Should().Be(500_000);
        quote.UnusedCredit.Amount.Should().Be(150_000);
        quote.ProratedTargetAmount.Amount.Should().Be(250_000);
        quote.AmountDue.Amount.Should().Be(100_000);
        quote.ProratedTargetAmount.Should().Be(quote.UnusedCredit + quote.AmountDue);
        quote.PeriodTo.Should().Be(ExpiresAt);
    }

    [Fact]
    public void Calculate_AtExactExpiry_UsesFullPriceAndOpensNewCycle()
    {
        var subscription = CreatePaidSubscription(300_000);
        var target = CreatePlan(500_000);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            ExpiresAt);

        quote.IsProrated.Should().BeFalse();
        quote.UnusedCredit.Should().Be(Money.Zero);
        quote.AmountDue.Amount.Should().Be(500_000);
        quote.PeriodFrom.Should().Be(ExpiresAt);
        quote.PeriodTo.Should().Be(ExpiresAt.AddMonths(1));
    }

    [Fact]
    public void Calculate_WithOddRemainingRatio_RoundsEachCreditThenPreservesInvariant()
    {
        var subscription = CreatePaidSubscription(100_001);
        var target = CreatePlan(200_003);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            StartedAt.AddDays(20));

        quote.UnusedCredit.Amount.Should().Be(33_334);
        quote.ProratedTargetAmount.Amount.Should().Be(66_668);
        quote.AmountDue.Amount.Should().Be(33_334);
        quote.ProratedTargetAmount.Should().Be(quote.UnusedCredit + quote.AmountDue);
    }

    [Fact]
    public void Calculate_YearlyPaidSubscription_ProrationKeepsAnnualExpiry()
    {
        var startedAt = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var expiresAt = startedAt.AddYears(1);
        var subscription = OperatorSubscription.CreateActiveTrial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            startedAt,
            expiresAt);
        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.WALLET);
        subscription.ActivatePaid(
            subscription.PlanId,
            SubscriptionBillingPeriod.YEARLY,
            SubscriptionPaymentMethod.WALLET,
            startedAt,
            expiresAt,
            Money.FromRaw(3_000_000),
            false);
        var target = CreatePlan(500_000);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.YEARLY,
            startedAt.AddMonths(6));

        quote.IsProrated.Should().BeTrue();
        quote.TargetCyclePrice.Amount.Should().Be(5_000_000);
        quote.PeriodTo.Should().Be(expiresAt);
    }

    [Fact]
    public void Calculate_TrialSubscription_ChargesFullPriceAndStartsAtDecisionInstant()
    {
        var subscription = OperatorSubscription.CreateActiveTrial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartedAt,
            ExpiresAt);
        var target = CreatePlan(500_000);
        var decisionAt = StartedAt.AddDays(10);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            decisionAt);

        quote.IsProrated.Should().BeFalse();
        quote.AmountDue.Amount.Should().Be(500_000);
        quote.PeriodFrom.Should().Be(decisionAt);
        quote.PeriodTo.Should().Be(decisionAt.AddMonths(1));
    }

    [Fact]
    public void Calculate_OneMinuteBeforeExpiry_RemainsOnProratedPath()
    {
        var subscription = CreatePaidSubscription(300_000);
        var target = CreatePlan(500_000);

        var quote = SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            ExpiresAt.AddMinutes(-1));

        quote.IsProrated.Should().BeTrue();
        quote.PeriodTo.Should().Be(ExpiresAt);
        quote.AmountDue.Amount.Should().BePositive();
    }

    [Fact]
    public void Calculate_ActivePaidSubscriptionWithDifferentBillingPeriod_RejectsUpgrade()
    {
        var subscription = CreatePaidSubscription(300_000);
        var target = CreatePlan(500_000);

        var action = () => SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.YEARLY,
            StartedAt.AddDays(15));

        action.Should().Throw<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "SUBSCRIPTION_UPGRADE_BILLING_PERIOD_MISMATCH");
    }

    [Fact]
    public void Calculate_TargetPriceNotHigher_RejectsNonPayableUpgrade()
    {
        var subscription = CreatePaidSubscription(500_000);
        var target = CreatePlan(300_000);

        var action = () => SubscriptionUpgradePricing.Calculate(
            subscription,
            target,
            SubscriptionBillingPeriod.MONTHLY,
            StartedAt.AddDays(15));

        action.Should().Throw<CodedValidationException>()
            .Where(exception => exception.ErrorCode == "SUBSCRIPTION_UPGRADE_AMOUNT_NOT_PAYABLE");
    }

    private static OperatorSubscription CreatePaidSubscription(long cyclePrice)
    {
        var subscription = OperatorSubscription.CreateActiveTrial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartedAt,
            ExpiresAt);
        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.WALLET);
        subscription.ActivatePaid(
            subscription.PlanId,
            SubscriptionBillingPeriod.MONTHLY,
            SubscriptionPaymentMethod.WALLET,
            StartedAt,
            ExpiresAt,
            Money.FromRaw(cyclePrice),
            false);
        return subscription;
    }

    private static SubscriptionPlan CreatePlan(long monthlyPrice)
        => SubscriptionPlan.Create(
            "Target",
            null,
            Money.FromRaw(monthlyPrice),
            Money.FromRaw(5_000_000),
            20,
            20,
            20,
            20,
            20,
            1_000,
            true,
            true,
            true);
}
