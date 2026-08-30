using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Subscriptions.RetrySubscriptionPayment;
using VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Subscriptions;

public sealed class UpgradeSubscriptionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublicUpgradeRequest_DoesNotExposeClientControlledReturnUrl()
    {
        typeof(SubscriptionUpgradeRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain("ReturnUrl");
    }

    [Fact]
    public void PendingPayment_KeepsActiveEntitlementPlanAndUsesFifteenMinuteAttemptDeadline()
    {
        var activePlanId = Guid.NewGuid();
        var subscription = OperatorSubscription.CreateActiveTrial(
            Guid.NewGuid(),
            activePlanId,
            Now.AddDays(-1),
            Now.AddDays(29));
        var attempt = SubscriptionUpgradeAttempt.Create(
            subscription.Id,
            subscription.OperatorId,
            Guid.NewGuid(),
            SubscriptionBillingPeriod.MONTHLY,
            Money.FromRaw(500_000),
            SubscriptionPaymentMethod.VNPAY,
            "idem-vnpay",
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            Now,
            Now.AddMinutes(15));

        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.VNPAY);

        subscription.PlanId.Should().Be(activePlanId);
        subscription.Status.Should().Be(SubscriptionStatus.PENDING_PAYMENT);
        attempt.DueAt.Should().Be(Now.AddMinutes(15));
        attempt.LatestPaymentStatus.Should().Be(SubscriptionPaymentSessionStatus.NONE);
    }

    [Fact]
    public async Task Handle_WalletUpgrade_BuildsTrustedSnapshotAndReturnsActiveContract()
    {
        var operatorId = Guid.NewGuid();
        var targetPlan = SubscriptionPlan.Create(
            "Pro",
            null,
            Money.FromRaw(500_000),
            Money.FromRaw(5_000_000),
            20,
            20,
            10,
            10,
            20,
            1_000,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-10),
            Now.AddDays(20));
        var operatorTenant = Operator.CreateApproved(
            "VietRide Bus",
            "BRN-001",
            "0312345678",
            "billing@vietride.test",
            "+84901234567",
            Guid.NewGuid(),
            Now.AddDays(-30));
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var payments = Substitute.For<ISubscriptionPaymentClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        plans.GetByIdAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        plans.GetByIdForUpdateAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>()).Returns(subscription);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(operatorTenant);
        SubscriptionUpgradeAttempt? createdAttempt = null;
        attempts.AddAsync(
                Arg.Do<SubscriptionUpgradeAttempt>(attempt => createdAttempt = attempt),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SubscriptionUpgradeAttempt>());
        attempts.GetByIdForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => createdAttempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>())
            .Returns(subscription);
        SubscriptionPaymentCreationRequest? captured = null;
        payments.CreateAsync(Arg.Do<SubscriptionPaymentCreationRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPaymentCreationResult(Guid.NewGuid(), "SUCCEEDED", null, "PENDING"));
        var handler = new UpgradeSubscriptionCommandHandler(
            subscriptions,
            plans,
            attempts,
            operators,
            payments,
            unitOfWork,
            clock);

        var result = await handler.Handle(
            new UpgradeSubscriptionCommand(
                operatorId,
                targetPlan.Id,
                "MONTHLY",
                "WALLET",
                "idem-wallet",
                "203.0.113.10"),
            CancellationToken.None);

        result.Status.Should().Be("ACTIVE");
        result.InvoiceStatus.Should().Be("PENDING");
        result.PaymentRedirectUrl.Should().BeNull();
        result.DueAt.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.OperatorId.Should().Be(operatorId);
        captured.PaymentMethod.Should().Be("WALLET");
        captured.ReturnMode.Should().Be("OPERATOR_WEB");
        captured.Snapshot.PlanName.Should().Be("Pro");
        captured.Snapshot.PeriodFrom.Should().Be(Now);
        captured.Snapshot.PeriodTo.Should().Be(Now.AddMonths(1));
        captured.Snapshot.BuyerSnapshot.TaxCode.Should().Be("0312345678");
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.PaymentMethod.Should().Be(SubscriptionPaymentMethod.WALLET);
        subscription.BillingPeriod.Should().Be(SubscriptionBillingPeriod.MONTHLY);
        createdAttempt.Should().NotBeNull();
        createdAttempt!.Status.Should().Be(SubscriptionUpgradeAttemptStatus.SUCCEEDED);
        createdAttempt.PaymentMethod.Should().Be(SubscriptionPaymentMethod.WALLET);
        createdAttempt.PaymentId.Should().Be(result.PaymentId);
        subscription.PlanId.Should().Be(targetPlan.Id);
        await unitOfWork.Received(2).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletRetryWithNewKey_ReusesInitiatedAttemptAfterInsufficientBalance()
    {
        var operatorId = Guid.NewGuid();
        var targetPlan = SubscriptionPlan.Create(
            "Pro",
            null,
            Money.FromRaw(500_000),
            Money.FromRaw(5_000_000),
            20,
            20,
            10,
            10,
            20,
            1_000,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-10),
            Now.AddDays(20));
        var attempt = SubscriptionUpgradeAttempt.CreateQuote(
            subscription.Id,
            operatorId,
            subscription.PlanId,
            targetPlan.Id,
            SubscriptionBillingPeriod.MONTHLY,
            targetPlan.PricePerMonth,
            SubscriptionPaymentMethod.WALLET,
            "old-insufficient-key",
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            Now,
            Now.AddMinutes(15),
            Now,
            Now.AddMonths(1),
            Money.Zero,
            targetPlan.PricePerMonth,
            Money.Zero,
            targetPlan.PricePerMonth,
            false);
        var operatorTenant = Operator.CreateApproved(
            "VietRide Bus",
            "BRN-001",
            "0312345678",
            "billing@vietride.test",
            "+84901234567",
            Guid.NewGuid(),
            Now.AddDays(-30));
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var payments = Substitute.For<ISubscriptionPaymentClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(subscription);
        attempts.GetActiveBySubscriptionIdAsync(subscription.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);
        plans.GetByIdForUpdateAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(operatorTenant);
        attempts.GetByIdForUpdateAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        SubscriptionPaymentCreationRequest? captured = null;
        payments.CreateAsync(
                Arg.Do<SubscriptionPaymentCreationRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPaymentCreationResult(Guid.NewGuid(), "SUCCEEDED", null, "PENDING"));
        var handler = new UpgradeSubscriptionCommandHandler(
            subscriptions,
            plans,
            attempts,
            operators,
            payments,
            unitOfWork,
            clock);

        var result = await handler.Handle(
            new UpgradeSubscriptionCommand(
                operatorId,
                targetPlan.Id,
                "MONTHLY",
                "WALLET",
                "new-retry-key",
                "203.0.113.10"),
            CancellationToken.None);

        result.UpgradeAttemptId.Should().Be(attempt.Id);
        result.Status.Should().Be("ACTIVE");
        captured.Should().NotBeNull();
        captured!.IdempotencyKey.Should().Be("new-retry-key");
        attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.SUCCEEDED);
        subscription.PlanId.Should().Be(targetPlan.Id);
        await attempts.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task RetryVnPay_UsesQuotedPeriodSnapshotInsteadOfCreatedAtCycle()
    {
        var operatorId = Guid.NewGuid();
        var sourcePlan = SubscriptionPlan.Create(
            "Current",
            null,
            Money.FromRaw(300_000),
            Money.FromRaw(3_000_000),
            10,
            10,
            10,
            10,
            10,
            500,
            true,
            true,
            true);
        var targetPlan = SubscriptionPlan.Create(
            "Target",
            null,
            Money.FromRaw(500_000),
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
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            sourcePlan.Id,
            Now.AddDays(-15),
            Now.AddDays(15));
        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.VNPAY);
        var attempt = SubscriptionUpgradeAttempt.CreateQuote(
            subscription.Id,
            operatorId,
            sourcePlan.Id,
            targetPlan.Id,
            SubscriptionBillingPeriod.MONTHLY,
            Money.FromRaw(100_000),
            SubscriptionPaymentMethod.VNPAY,
            "initial-vnpay",
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            Now.AddMinutes(-5),
            Now.AddMinutes(10),
            Now.AddMinutes(-5),
            Now.AddDays(10),
            Money.FromRaw(300_000),
            Money.FromRaw(500_000),
            Money.FromRaw(150_000),
            Money.FromRaw(250_000),
            true);
        var failedPaymentId = Guid.NewGuid();
        attempt.BindPendingPayment(failedPaymentId);
        attempt.MarkPaymentFailed(failedPaymentId);
        var operatorTenant = Operator.CreateApproved(
            "VietRide Bus",
            "BRN-001",
            "0312345678",
            "billing@vietride.test",
            "+84901234567",
            Guid.NewGuid(),
            Now.AddDays(-30));
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var payments = Substitute.For<ISubscriptionPaymentClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        attempts.GetByIdForUpdateAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        subscriptions.GetByIdAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        plans.GetByIdAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        plans.GetByIdAsync(sourcePlan.Id, Arg.Any<CancellationToken>()).Returns(sourcePlan);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(operatorTenant);
        SubscriptionPaymentCreationRequest? captured = null;
        payments.CreateAsync(
                Arg.Do<SubscriptionPaymentCreationRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPaymentCreationResult(
                Guid.NewGuid(),
                "PENDING_REDIRECT",
                "https://sandbox.vnpay.test/pay",
                null));
        var handler = new RetrySubscriptionPaymentCommandHandler(
            attempts,
            subscriptions,
            plans,
            operators,
            payments,
            unitOfWork,
            clock);

        await handler.Handle(
            new RetrySubscriptionPaymentCommand(
                operatorId,
                attempt.Id,
                "retry-vnpay",
                "203.0.113.10"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Snapshot.PeriodFrom.Should().Be(attempt.PeriodFrom);
        captured.Snapshot.PeriodTo.Should().Be(attempt.PeriodTo);
        captured.Snapshot.PeriodFrom.Should().NotBe(attempt.CreatedAt);
    }

    [Theory]
    [InlineData("VNPAY", true)]
    [InlineData("WALLET", true)]
    [InlineData("CARD", false)]
    public void Validator_EnforcesSupportedPaymentMethod(string method, bool valid)
    {
        var validator = new UpgradeSubscriptionCommandValidator();

        var result = validator.Validate(new UpgradeSubscriptionCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MONTHLY",
            method,
            "idem",
            "203.0.113.10"));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Handle_ReplayedIdempotencyKeyWithDifferentPaymentMethod_ThrowsMismatch()
    {
        var operatorId = Guid.NewGuid();
        var targetPlan = SubscriptionPlan.Create(
            "Pro",
            null,
            Money.FromRaw(500_000),
            Money.FromRaw(5_000_000),
            20,
            20,
            10,
            10,
            20,
            1_000,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-10),
            Now.AddDays(20));
        var replay = SubscriptionUpgradeAttempt.Create(
            subscription.Id,
            operatorId,
            targetPlan.Id,
            SubscriptionBillingPeriod.MONTHLY,
            targetPlan.PricePerMonth,
            SubscriptionPaymentMethod.WALLET,
            "idem-replay",
            Now,
            Now.AddMinutes(15));
        var operatorTenant = Operator.CreateApproved(
            "VietRide Bus",
            "BRN-001",
            "0312345678",
            "billing@vietride.test",
            "+84901234567",
            Guid.NewGuid(),
            Now.AddDays(-30));
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var plans = Substitute.For<ISubscriptionPlanRepository>();
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var payments = Substitute.For<ISubscriptionPaymentClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        plans.GetByIdAsync(targetPlan.Id, Arg.Any<CancellationToken>()).Returns(targetPlan);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(operatorTenant);
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>()).Returns(subscription);
        attempts.GetByIdempotencyKeyAsync("idem-replay", Arg.Any<CancellationToken>()).Returns(replay);
        var handler = new UpgradeSubscriptionCommandHandler(
            subscriptions,
            plans,
            attempts,
            operators,
            payments,
            unitOfWork,
            clock);

        var action = () => handler.Handle(
            new UpgradeSubscriptionCommand(
                operatorId,
                targetPlan.Id,
                "MONTHLY",
                "VNPAY",
                "idem-replay",
                "203.0.113.10"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("IDEMPOTENCY_KEY_MISMATCH");
        await payments.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task PaymentSucceededEvent_Wallet_ActivatesUsingCanonicalSnapshotPeriod()
    {
        var operatorId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorId,
            SubscriptionPlan.StarterPlanId,
            Now.AddDays(-10),
            Now.AddDays(20));
        var attempt = SubscriptionUpgradeAttempt.Create(
            subscription.Id,
            operatorId,
            planId,
            SubscriptionBillingPeriod.MONTHLY,
            Money.FromRaw(500_000),
            SubscriptionPaymentMethod.WALLET,
            "idem-wallet",
            Now,
            Now.AddMinutes(15));
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<bool>>>()());
        attempts.GetByIdForUpdateAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        var activation = new SubscriptionPaymentActivationService(
            attempts,
            subscriptions,
            unitOfWork,
            NullLogger<SubscriptionPaymentActivationService>.Instance);
        var handler = new SubscriptionPaymentSucceededIntegrationEventHandler(activation);

        await handler.HandleAsync(
            new SubscriptionPaymentSucceededIntegrationEvent
            {
                PaymentId = paymentId,
                UpgradeAttemptId = attempt.Id,
                OperatorId = operatorId,
                OperatorSubscriptionId = subscription.Id,
                PlanId = planId,
                Amount = 500_000,
                Method = "WALLET",
                PlanName = "Pro",
                BillingPeriod = "MONTHLY",
                SucceededAt = Now,
                PeriodFrom = Now.AddTicks(8),
                PeriodTo = Now.AddMonths(1).AddTicks(8),
                BuyerSnapshot = new VietRide.Identity.Infrastructure.Messaging.SubscriptionBuyerSnapshot
                {
                    Name = "VietRide Bus",
                    BusinessRegistrationNumber = "BRN-001",
                    TaxCode = "0312345678",
                    ContactEmail = "billing@vietride.test",
                    ContactPhone = "+84901234567",
                },
            },
            CancellationToken.None);

        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.PaymentMethod.Should().Be(SubscriptionPaymentMethod.WALLET);
        subscription.StartedAt.Should().Be(Now);
        subscription.ExpiresAt.Should().Be(Now.AddMonths(1));
        attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.SUCCEEDED);
        attempt.PaymentId.Should().Be(paymentId);
        await unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<Task<bool>>>(),
            Arg.Any<CancellationToken>());

        await handler.HandleAsync(
            new SubscriptionPaymentSucceededIntegrationEvent
            {
                PaymentId = paymentId,
                UpgradeAttemptId = attempt.Id,
                OperatorId = operatorId,
                OperatorSubscriptionId = subscription.Id,
                Amount = 500_000,
                Method = "WALLET",
                PlanName = "Pro",
                BillingPeriod = "MONTHLY",
                PeriodFrom = Now,
                PeriodTo = Now.AddMonths(1),
            },
            CancellationToken.None);

        subscriptions.Received(1).Update(subscription);
        attempts.Received(1).Update(attempt);
    }
}
