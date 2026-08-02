using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators;

public sealed class PendingPaymentEntitlementTests
{
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IncrementUsage_PendingPayment_UsesReturnedActivePlanEntitlement()
    {
        var activePlan = SubscriptionPlan.CreateStarter();
        var current = CreateActiveSubscription();
        current.MoveToPendingPayment(SubscriptionPaymentMethod.VNPAY);
        var updated = CreateActiveSubscription();
        updated.IncrementUsage(SubscriptionUsageResource.DRIVERS);
        updated.MoveToPendingPayment(SubscriptionPaymentMethod.VNPAY);
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        subscriptions.GetCurrentWithPlanByOperatorIdAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns((current, activePlan));
        subscriptions.TryIncrementUsageWithinLimitAsync(
                OperatorId,
                SubscriptionUsageResource.DRIVERS,
                1,
                Arg.Any<CancellationToken>())
            .Returns((updated, activePlan));
        var handler = CreateHandler(subscriptions);

        var result = await handler.Handle(
            new IncrementOperatorUsageCommand(OperatorId, SubscriptionUsageResource.DRIVERS.ToString(), 1),
            CancellationToken.None);

        result.Status.Should().Be(SubscriptionStatus.PENDING_PAYMENT.ToString());
        result.Plan.PlanId.Should().Be(activePlan.Id);
        result.Usage.CurrentDrivers.Should().Be(1);
        await subscriptions.Received(1).TryIncrementUsageWithinLimitAsync(
            OperatorId,
            SubscriptionUsageResource.DRIVERS,
            1,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionStatus.EXPIRED)]
    [InlineData(SubscriptionStatus.CANCELLED)]
    public async Task IncrementUsage_TerminalSubscription_DoesNotReachAtomicIncrement(SubscriptionStatus status)
    {
        var subscription = status == SubscriptionStatus.EXPIRED
            ? CreateExpiredSubscription()
            : CreateCancelledSubscription();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        subscriptions.GetCurrentWithPlanByOperatorIdAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns((subscription, SubscriptionPlan.CreateStarter()));
        var handler = CreateHandler(subscriptions);

        var act = () => handler.Handle(
            new IncrementOperatorUsageCommand(OperatorId, SubscriptionUsageResource.DRIVERS.ToString(), 1),
            CancellationToken.None);

        if (status == SubscriptionStatus.EXPIRED)
        {
            var assertion = await act.Should().ThrowAsync<IdentityDomainException>();
            assertion.Which.ErrorCode.Should().Be("SUBSCRIPTION_EXPIRED");
        }
        else
        {
            await act.Should().ThrowAsync<ValidationException>();
        }

        await subscriptions.DidNotReceiveWithAnyArgs().TryIncrementUsageWithinLimitAsync(
            default,
            default,
            default,
            default);
    }

    private static IncrementOperatorUsageCommandHandler CreateHandler(
        IOperatorSubscriptionRepository subscriptions)
    {
        var operators = Substitute.For<IOperatorRepository>();
        operators.ExistsAsync(OperatorId, Arg.Any<CancellationToken>()).Returns(true);
        return new IncrementOperatorUsageCommandHandler(
            operators,
            subscriptions,
            Substitute.For<ISubscriptionUsageWarningPublisher>());
    }

    private static OperatorSubscription CreateActiveSubscription()
        => OperatorSubscription.CreateActiveTrial(
            OperatorId,
            SubscriptionPlan.StarterPlanId,
            Now,
            Now.AddDays(30));

    private static OperatorSubscription CreateExpiredSubscription()
    {
        var subscription = CreateActiveSubscription();
        subscription.MarkExpired(Now.AddDays(31));
        return subscription;
    }

    private static OperatorSubscription CreateCancelledSubscription()
    {
        var subscription = OperatorSubscription.CreatePendingApproval(
            OperatorId,
            SubscriptionPlan.StarterPlanId,
            Now);
        subscription.CancelPendingApproval();
        return subscription;
    }
}
