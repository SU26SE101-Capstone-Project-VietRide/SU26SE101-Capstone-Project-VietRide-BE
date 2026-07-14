using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Subscriptions;

public sealed class UpgradeSubscriptionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

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
        subscriptions.GetCurrentByOperatorIdForUpdateAsync(operatorId, Arg.Any<CancellationToken>()).Returns(subscription);
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>()).Returns(operatorTenant);
        SubscriptionUpgradeAttempt? createdAttempt = null;
        attempts.AddAsync(
                Arg.Do<SubscriptionUpgradeAttempt>(attempt => createdAttempt = attempt),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SubscriptionUpgradeAttempt>());
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
                null,
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
        captured.Snapshot.PlanName.Should().Be("Pro");
        captured.Snapshot.PeriodFrom.Should().Be(Now);
        captured.Snapshot.PeriodTo.Should().Be(Now.AddMonths(1));
        captured.Snapshot.BuyerSnapshot.TaxCode.Should().Be("0312345678");
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.PaymentMethod.Should().BeNull();
        createdAttempt.Should().NotBeNull();
        createdAttempt!.Status.Should().Be(SubscriptionUpgradeAttemptStatus.INITIATED);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("VNPAY", null, false)]
    [InlineData("WALLET", "https://app.vietride.test/result", false)]
    [InlineData("VNPAY", "https://app.vietride.test/result", true)]
    [InlineData("WALLET", null, true)]
    public void Validator_EnforcesMethodSpecificReturnUrl(string method, string? returnUrl, bool valid)
    {
        var validator = new UpgradeSubscriptionCommandValidator();

        var result = validator.Validate(new UpgradeSubscriptionCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MONTHLY",
            method,
            returnUrl,
            "idem",
            "203.0.113.10"));

        result.IsValid.Should().Be(valid);
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
            "idem-wallet",
            Now,
            Now.AddDays(7));
        var attempts = Substitute.For<ISubscriptionUpgradeAttemptRepository>();
        var subscriptions = Substitute.For<IOperatorSubscriptionRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        attempts.GetByIdForUpdateAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        subscriptions.GetByIdForUpdateAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(subscription);
        var handler = new SubscriptionPaymentSucceededIntegrationEventHandler(
            attempts,
            subscriptions,
            unitOfWork,
            NullLogger<SubscriptionPaymentSucceededIntegrationEventHandler>.Instance);

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
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

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
