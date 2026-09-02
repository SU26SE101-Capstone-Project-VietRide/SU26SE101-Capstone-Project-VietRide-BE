using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;
using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;
using VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Subscriptions;

public sealed class SubscriptionQuoteAndCustomRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QuoteForeignCustomPlan_ReturnsMaskedNotFound()
    {
        var ownerOperatorId = Guid.NewGuid();
        var foreignOperatorId = Guid.NewGuid();
        var plan = SubscriptionPlan.CreateCustom(
            ownerOperatorId,
            Guid.NewGuid(),
            "Private Enterprise",
            null,
            Money.FromRaw(1_000_000),
            Money.Zero,
            50,
            50,
            50,
            20,
            50,
            5_000,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            foreignOperatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-1),
            Now.AddDays(29));
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(foreignOperatorId, Arg.Any<CancellationToken>())
            .Returns(subscription);
        plans.GetByIdForUpdateAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        var handler = new QuoteSubscriptionUpgradeCommandHandler(
            subscriptions,
            plans,
            attempts,
            Substitute.For<IUnitOfWork>(),
            clock);

        var action = () => handler.Handle(
            new QuoteSubscriptionUpgradeCommand(
                foreignOperatorId,
                plan.Id,
                "MONTHLY",
                "WALLET",
                Guid.NewGuid().ToString()),
            CancellationToken.None);

        await action.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Quote_ActivePaidSubscription_CreatesProratedSnapshot()
    {
        var operatorId = Guid.NewGuid();
        var sourcePlan = CreatePlan("Current", 300_000, 10);
        var targetPlan = CreatePlan("Target", 500_000, 20);
        var subscription = CreatePaidSubscription(operatorId, sourcePlan.Id, 300_000);
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(subscription);
        plans.GetByIdForUpdateAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        SubscriptionUpgradeAttempt? stored = null;
        attempts.AddAsync(
                Arg.Do<SubscriptionUpgradeAttempt>(attempt => stored = attempt),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SubscriptionUpgradeAttempt>());
        var handler = new QuoteSubscriptionUpgradeCommandHandler(
            subscriptions,
            plans,
            attempts,
            unitOfWork,
            clock);

        var result = await handler.Handle(
            new QuoteSubscriptionUpgradeCommand(
                operatorId,
                targetPlan.Id,
                "MONTHLY",
                "WALLET",
                Guid.NewGuid().ToString()),
            CancellationToken.None);

        result.ProrationApplied.Should().BeTrue();
        result.CurrentCyclePrice.Should().Be(300_000);
        result.TargetCyclePrice.Should().Be(500_000);
        result.AmountDue.Should().Be(100_000);
        stored.Should().NotBeNull();
        stored!.SourcePlanId.Should().Be(sourcePlan.Id);
        stored.PeriodTo.Should().Be(Now.AddDays(15));
    }

    [Fact]
    public async Task Confirm_WhenTargetPlanWasDeactivated_RejectsBeforePayment()
    {
        var fixture = CreateConfirmFixture();
        fixture.TargetPlan.Deactivate();

        var action = () => fixture.Handler.Handle(
            new ConfirmSubscriptionUpgradePaymentCommand(
                fixture.OperatorId,
                fixture.Attempt.Id,
                Guid.NewGuid().ToString(),
                "203.0.113.10"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE");
        await fixture.Payments.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Confirm_WalletInsufficient_KeepsAttemptInitiatedAndSubscriptionActive()
    {
        var fixture = CreateConfirmFixture();
        fixture.Payments.CreateAsync(Arg.Any<SubscriptionPaymentCreationRequest>(), Arg.Any<CancellationToken>())
            .Returns<SubscriptionPaymentCreationResult>(_ => throw new SubscriptionPaymentClientException(
                402,
                "WALLET_INSUFFICIENT_BALANCE",
                "Insufficient wallet balance."));

        var action = () => fixture.Handler.Handle(
            new ConfirmSubscriptionUpgradePaymentCommand(
                fixture.OperatorId,
                fixture.Attempt.Id,
                Guid.NewGuid().ToString(),
                "203.0.113.10"),
            CancellationToken.None);

        await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        fixture.Attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.INITIATED);
        fixture.Attempt.PaymentId.Should().BeNull();
        fixture.Attempt.LatestPaymentStatus.Should().Be(SubscriptionPaymentSessionStatus.NONE);
        fixture.Subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
    }

    [Fact]
    public async Task CreateCustomRequest_EnqueuesSubmittedEventWithOperatorSnapshot()
    {
        var operatorTenant = CreateOperator("Nhà xe Việt Ride");
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        subscriptions.GetCurrentByOperatorIdAsync(operatorTenant.Id, Arg.Any<CancellationToken>())
            .Returns(OperatorSubscription.CreateActiveTrial(
                operatorTenant.Id,
                SubscriptionPlan.StarterPlanId,
                Now.AddDays(-1),
                Now.AddDays(29)));
        operators.GetByIdNoTrackingAsync(operatorTenant.Id, Arg.Any<CancellationToken>())
            .Returns(operatorTenant);
        var handler = new CreateSubscriptionCustomRequestCommandHandler(
            requests,
            subscriptions,
            operators,
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var result = await handler.Handle(
            new CreateSubscriptionCustomRequestCommand(
                Guid.NewGuid(), operatorTenant.Id, 10, 10, 10, 10, 10, 100,
                true, false, true, "MONTHLY", "Cần gói riêng"),
            CancellationToken.None);

        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            SubscriptionCustomRequestSubmittedIntegrationEvent.EventType,
            Arg.Is<string>(payload => EventPayloadMatches(
                payload,
                result.RequestId,
                operatorTenant.Id,
                new KeyValuePair<string, string>("operatorName", operatorTenant.Name))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCustomRequest_WhenPendingExists_DoesNotEnqueueEvent()
    {
        var operatorId = Guid.NewGuid();
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        subscriptions.GetCurrentByOperatorIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(OperatorSubscription.CreateActiveTrial(
                operatorId,
                SubscriptionPlan.StarterPlanId,
                Now.AddDays(-1),
                Now.AddDays(29)));
        requests.GetPendingByOperatorIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(CreateCustomRequest(operatorId));
        var handler = new CreateSubscriptionCustomRequestCommandHandler(
            requests,
            subscriptions,
            Substitute.For<IOperatorRepository>(),
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var action = () => handler.Handle(
            new CreateSubscriptionCustomRequestCommand(
                Guid.NewGuid(), operatorId, 10, 10, 10, 10, 10, 100,
                true, false, true, "MONTHLY", null),
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedConflictException>()).Which.ErrorCode
            .Should().Be("CUSTOM_REQUEST_ALREADY_PENDING");
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ApproveCustomRequest_EnqueuesApprovedEventWithCreatedPlan()
    {
        var operatorId = Guid.NewGuid();
        var request = CreateCustomRequest(operatorId);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        requests.GetByIdForUpdateAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(OperatorSubscription.CreateActiveTrial(
                operatorId,
                SubscriptionPlan.StarterPlanId,
                Now.AddDays(-1),
                Now.AddDays(29)));
        var handler = new ApproveSubscriptionCustomRequestCommandHandler(
            requests,
            subscriptions,
            plans,
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var result = await handler.Handle(
            new ApproveSubscriptionCustomRequestCommand(
                Guid.NewGuid(), request.Id, "Doanh nghiệp riêng", null,
                1_000_000, 10_000_000, 10, 10, 10, 10, 10, 100,
                true, true, true),
            CancellationToken.None);

        result.ApprovedPlanId.Should().NotBeNull();
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            SubscriptionCustomRequestApprovedIntegrationEvent.EventType,
            Arg.Is<string>(payload => EventPayloadMatches(
                payload,
                request.Id,
                operatorId,
                new KeyValuePair<string, string>(
                    "approvedPlanId",
                    result.ApprovedPlanId!.Value.ToString()),
                new KeyValuePair<string, string>("planName", "Doanh nghiệp riêng"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectCustomRequest_EnqueuesRejectedEventWithTrimmedReason()
    {
        var operatorId = Guid.NewGuid();
        var request = CreateCustomRequest(operatorId);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        requests.GetByIdForUpdateAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new RejectSubscriptionCustomRequestCommandHandler(
            requests,
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        await handler.Handle(
            new RejectSubscriptionCustomRequestCommand(
                Guid.NewGuid(), request.Id, "  Hạn mức chưa phù hợp  "),
            CancellationToken.None);

        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            SubscriptionCustomRequestRejectedIntegrationEvent.EventType,
            Arg.Is<string>(payload => EventPayloadMatches(
                payload,
                request.Id,
                operatorId,
                new KeyValuePair<string, string>(
                    "rejectionReason",
                    "Hạn mức chưa phù hợp"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveCustomRequest_WhenAlreadyReviewed_DoesNotEnqueueEvent()
    {
        var request = CreateCustomRequest(Guid.NewGuid());
        request.Reject(Guid.NewGuid(), "Không phù hợp", Now);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        requests.GetByIdForUpdateAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new ApproveSubscriptionCustomRequestCommandHandler(
            requests,
            Substitute.For<IOperatorSubscriptionRepository>(),
            Substitute.For<ISubscriptionPlanRepository>(),
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var action = () => handler.Handle(
            new ApproveSubscriptionCustomRequestCommand(
                Guid.NewGuid(), request.Id, "Doanh nghiệp riêng", null,
                1_000_000, 10_000_000, 10, 10, 10, 10, 10, 100,
                true, true, true),
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedConflictException>()).Which.ErrorCode
            .Should().Be("CUSTOM_REQUEST_ALREADY_REVIEWED");
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task RejectCustomRequest_WhenAlreadyReviewed_DoesNotEnqueueEvent()
    {
        var request = CreateCustomRequest(Guid.NewGuid());
        request.Reject(Guid.NewGuid(), "Không phù hợp", Now);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        requests.GetByIdForUpdateAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new RejectSubscriptionCustomRequestCommandHandler(
            requests,
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var action = () => handler.Handle(
            new RejectSubscriptionCustomRequestCommand(
                Guid.NewGuid(), request.Id, "Lý do khác"),
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedConflictException>()).Which.ErrorCode
            .Should().Be("CUSTOM_REQUEST_ALREADY_REVIEWED");
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ApproveCustomRequest_WhenGrantedQuotaBelowUsage_RejectsWithoutCreatingPlan()
    {
        var operatorId = Guid.NewGuid();
        var request = SubscriptionCustomRequest.Create(
            operatorId,
            20,
            20,
            20,
            20,
            20,
            1_000,
            true,
            true,
            true,
            SubscriptionBillingPeriod.MONTHLY,
            null);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-1),
            Now.AddDays(29));
        subscription.IncrementUsage(SubscriptionUsageResource.VEHICLES, 3);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        requests.GetByIdForUpdateAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(subscription);
        var handler = new ApproveSubscriptionCustomRequestCommandHandler(
            requests,
            subscriptions,
            plans,
            Substitute.For<IActivityLogRepository>(),
            outbox,
            CreateClock());

        var action = () => handler.Handle(
            new ApproveSubscriptionCustomRequestCommand(
                Guid.NewGuid(),
                request.Id,
                "Enterprise",
                null,
                1_000_000,
                10_000_000,
                2,
                20,
                20,
                20,
                20,
                1_000,
                true,
                true,
                true),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("CUSTOM_PLAN_LIMIT_BELOW_CURRENT_USAGE");
        exception.Which.Errors.Should().Contain(error => error.Field == "maxVehicles"
            && error.Message.Contains("requested 20", StringComparison.Ordinal)
            && error.Message.Contains("granted 2", StringComparison.Ordinal)
            && error.Message.Contains("current usage 3", StringComparison.Ordinal));
        await plans.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task GetCustomRequest_WithForeignOperatorId_ReturnsNotFound()
    {
        var request = SubscriptionCustomRequest.Create(
            Guid.NewGuid(),
            10,
            10,
            10,
            10,
            10,
            100,
            false,
            false,
            false,
            SubscriptionBillingPeriod.MONTHLY,
            null);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new GetSubscriptionCustomRequestQueryHandler(requests);

        var action = () => handler.Handle(
            new GetSubscriptionCustomRequestQuery(request.Id, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAdminCustomRequests_ReturnsOperatorNamesWithSingleBulkLookup()
    {
        var firstOperator = CreateOperator("Alpha Transit");
        var secondOperator = CreateOperator("Beta Transit");
        var firstRequest = CreateCustomRequest(firstOperator.Id);
        var secondRequest = CreateCustomRequest(secondOperator.Id);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        requests.ListForAdminAsync(
                SubscriptionCustomRequestStatus.PENDING_REVIEW,
                Arg.Any<CancellationToken>())
            .Returns(new[] { secondRequest, firstRequest });
        var operators = Substitute.For<IOperatorRepository>();
        operators.ListSummariesByIdsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2
                    && ids.Contains(firstOperator.Id)
                    && ids.Contains(secondOperator.Id)),
                Arg.Any<CancellationToken>())
            .Returns(new[] { firstOperator, secondOperator });
        var handler = new ListAdminSubscriptionCustomRequestsQueryHandler(requests, operators);

        var result = await handler.Handle(
            new ListAdminSubscriptionCustomRequestsQuery(SubscriptionCustomRequestStatus.PENDING_REVIEW.ToString()),
            CancellationToken.None);

        result.Select(item => (item.OperatorId, item.OperatorName)).Should().Equal(
            (secondOperator.Id, secondOperator.Name),
            (firstOperator.Id, firstOperator.Name));
        await operators.Received(1).ListSummariesByIdsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAdminCustomRequest_ReturnsOperatorName()
    {
        var operatorTenant = CreateOperator("Gamma Transit");
        var request = CreateCustomRequest(operatorTenant.Id);
        var requests = Substitute.For<ISubscriptionCustomRequestRepository>();
        requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var operators = Substitute.For<IOperatorRepository>();
        operators.ListSummariesByIdsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { operatorTenant.Id })),
                Arg.Any<CancellationToken>())
            .Returns(new[] { operatorTenant });
        var handler = new GetAdminSubscriptionCustomRequestQueryHandler(requests, operators);

        var result = await handler.Handle(
            new GetAdminSubscriptionCustomRequestQuery(request.Id),
            CancellationToken.None);

        result.OperatorId.Should().Be(operatorTenant.Id);
        result.OperatorName.Should().Be(operatorTenant.Name);
    }

    private static ConfirmFixture CreateConfirmFixture()
    {
        var operatorId = Guid.NewGuid();
        var sourcePlan = CreatePlan("Current", 300_000, 10);
        var targetPlan = CreatePlan("Target", 500_000, 20);
        var subscription = CreatePaidSubscription(operatorId, sourcePlan.Id, 300_000);
        var price = VietRide.Identity.Application.Features.Subscriptions.SubscriptionUpgradePricing.Calculate(
            subscription,
            targetPlan,
            SubscriptionBillingPeriod.MONTHLY,
            Now);
        var attempt = SubscriptionUpgradeAttempt.CreateQuote(
            subscription.Id,
            operatorId,
            sourcePlan.Id,
            targetPlan.Id,
            SubscriptionBillingPeriod.MONTHLY,
            price.AmountDue,
            SubscriptionPaymentMethod.WALLET,
            Guid.NewGuid().ToString(),
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            Now,
            price.DueAt,
            price.PeriodFrom,
            price.PeriodTo,
            price.CurrentCyclePrice,
            price.TargetCyclePrice,
            price.UnusedCredit,
            price.ProratedTargetAmount,
            price.IsProrated);
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var payments = Substitute.For<ISubscriptionPaymentClient>();
        attempts.GetByIdForUpdateAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        plans.GetByIdForUpdateAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(
            Operator.CreateApproved(
                "VietRide Bus",
                "BRN-001",
                "0312345678",
                "billing@vietride.test",
                "+84901234567",
                Guid.NewGuid(),
                Now.AddDays(-100)));
        var handler = new ConfirmSubscriptionUpgradePaymentCommandHandler(
            attempts,
            subscriptions,
            plans,
            operators,
            payments,
            Substitute.For<IUnitOfWork>(),
            CreateClock());
        return new ConfirmFixture(operatorId, subscription, targetPlan, attempt, payments, handler);
    }

    private static Operator CreateOperator(string name)
        => Operator.CreatePending(
            name,
            $"BR-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"{Guid.NewGuid():N}@example.com",
            "+84901234567");

    private static SubscriptionCustomRequest CreateCustomRequest(Guid operatorId)
        => SubscriptionCustomRequest.Create(
            operatorId,
            10,
            10,
            10,
            10,
            10,
            100,
            false,
            false,
            false,
            SubscriptionBillingPeriod.MONTHLY,
            null);

    private static OperatorSubscription CreatePaidSubscription(Guid operatorId, Guid planId, long cyclePrice)
    {
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            planId,
            Now.AddDays(-15),
            Now.AddDays(15));
        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.WALLET);
        subscription.ActivatePaid(
            planId,
            SubscriptionBillingPeriod.MONTHLY,
            SubscriptionPaymentMethod.WALLET,
            Now.AddDays(-15),
            Now.AddDays(15),
            Money.FromRaw(cyclePrice),
            false);
        return subscription;
    }

    private static SubscriptionPlan CreatePlan(string name, long monthlyPrice, int maxVehicles)
        => SubscriptionPlan.Create(
            name,
            null,
            Money.FromRaw(monthlyPrice),
            Money.FromRaw(5_000_000),
            maxVehicles,
            20,
            20,
            20,
            20,
            1_000,
            true,
            true,
            true);

    private static IClock CreateClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static bool EventPayloadMatches(
        string payload,
        Guid requestId,
        Guid operatorId,
        params KeyValuePair<string, string>[] expectedStrings)
    {
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("eventId", out var eventId)
            || !Guid.TryParse(eventId.GetString(), out var parsedEventId)
            || parsedEventId == Guid.Empty
            || !root.TryGetProperty("occurredAt", out var occurredAt)
            || !DateTimeOffset.TryParse(occurredAt.GetString(), out _)
            || root.GetProperty("requestId").GetGuid() != requestId
            || root.GetProperty("operatorId").GetGuid() != operatorId)
        {
            return false;
        }

        return expectedStrings.All(expected =>
            root.GetProperty(expected.Key).GetString() == expected.Value);
    }

    private sealed record ConfirmFixture(
        Guid OperatorId,
        OperatorSubscription Subscription,
        SubscriptionPlan TargetPlan,
        SubscriptionUpgradeAttempt Attempt,
        ISubscriptionPaymentClient Payments,
        ConfirmSubscriptionUpgradePaymentCommandHandler Handler);
}
