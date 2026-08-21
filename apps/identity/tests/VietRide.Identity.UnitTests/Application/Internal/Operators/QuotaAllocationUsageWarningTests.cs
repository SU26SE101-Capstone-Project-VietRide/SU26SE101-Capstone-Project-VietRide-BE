using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.QuotaAllocations;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators;

public sealed class QuotaAllocationUsageWarningTests
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimQuotaAllocation_AfterIncrement_DelegatesDurableWarningCheck()
    {
        var plan = SubscriptionPlan.CreateStarter();
        var current = OperatorSubscription.CreateActiveTrial(
            OperatorId,
            plan.Id,
            Now,
            Now.AddDays(30));
        var updated = OperatorSubscription.CreateActiveTrial(
            OperatorId,
            plan.Id,
            Now,
            Now.AddDays(30));
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        subscriptions.GetCurrentWithPlanByOperatorIdAsync(
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns((current, plan));
        subscriptions.TryIncrementUsageWithinLimitAsync(
                OperatorId,
                SubscriptionUsageResource.VEHICLES,
                1,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((updated, plan));
        var allocations = Substitute.For<ISubscriptionQuotaAllocationRepository>();
        allocations.GetActiveAsync(
                OperatorId,
                SubscriptionUsageResource.VEHICLES,
                ResourceId,
                Arg.Any<CancellationToken>())
            .Returns((SubscriptionQuotaAllocation?)null);
        allocations.AddAsync(
                Arg.Any<SubscriptionQuotaAllocation>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<SubscriptionQuotaAllocation>(0));
        var usageWarnings = Substitute.For<ISubscriptionUsageWarningPublisher>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(1));
        var handler = new ClaimQuotaAllocationCommandHandler(
            subscriptions,
            allocations,
            usageWarnings,
            clock);

        var result = await handler.Handle(
            new ClaimQuotaAllocationCommand(
                OperatorId,
                SubscriptionUsageResource.VEHICLES.ToString(),
                ResourceId,
                null),
            CancellationToken.None);

        result.Resource.Should().Be("VEHICLES");
        await usageWarnings.Received(1).EnqueueIfThresholdCrossedAsync(
            updated,
            plan,
            SubscriptionUsageResource.VEHICLES,
            1,
            null,
            Arg.Any<CancellationToken>());
    }
}
