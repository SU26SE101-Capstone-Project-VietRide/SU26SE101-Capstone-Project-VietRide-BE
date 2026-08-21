using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Identity.IntegrationTests.Api;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Inbox;

namespace VietRide.Identity.IntegrationTests;

public sealed class SubscriptionPaymentInboxTests
{
    [Fact]
    public async Task TransactionalInbox_ProcessesSucceededAndExpiredSubscriptionPaymentsAtomically()
    {
        using var factory = new AdminUsersEndpointsTests.DbBackedAdminUsersFactory();
        try
        {
            await factory.InitializeAsync();
            var succeeded = await SeedPendingUpgradeAsync(factory, "succeeded");
            var expired = await SeedPendingUpgradeAsync(factory, "expired");

            await ProcessSucceededAsync(factory, succeeded);
            await ProcessExpiredAsync(factory, expired);

            await AssertSucceededAsync(factory, succeeded);
            await AssertExpiredAsync(factory, expired);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    private static async Task<PendingUpgradeSeed> SeedPendingUpgradeAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        string suffix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var rawNow = DateTimeOffset.UtcNow;
        var now = rawNow.AddTicks(-(rawNow.Ticks % TimeSpan.TicksPerMicrosecond));
        var paymentId = Guid.NewGuid();
        var operatorTenant = Operator.CreatePending(
            $"Inbox Test {suffix}",
            $"BR-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"inbox-{suffix}-{Guid.NewGuid():N}@example.com",
            "+84901234567");
        var currentPlan = SubscriptionPlan.Create(
            $"Current {suffix} {Guid.NewGuid():N}",
            null,
            Money.FromRaw(100_000),
            Money.FromRaw(1_000_000),
            3,
            5,
            5,
            3,
            5,
            100,
            false,
            false,
            true);
        var targetPlan = SubscriptionPlan.Create(
            $"Target {suffix} {Guid.NewGuid():N}",
            null,
            Money.FromRaw(200_000),
            Money.FromRaw(2_000_000),
            10,
            20,
            20,
            10,
            20,
            500,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorTenant.Id,
            currentPlan.Id,
            now.AddDays(-1),
            now.AddDays(29));
        subscription.MoveToPendingPayment(SubscriptionPaymentMethod.VNPAY);
        var attempt = SubscriptionUpgradeAttempt.Create(
            subscription.Id,
            operatorTenant.Id,
            targetPlan.Id,
            SubscriptionBillingPeriod.MONTHLY,
            targetPlan.PricePerMonth,
            SubscriptionPaymentMethod.VNPAY,
            $"inbox-{suffix}-{Guid.NewGuid():N}",
            now,
            now.AddMinutes(15));
        attempt.BindPendingPayment(paymentId);

        await db.Operators.AddAsync(operatorTenant);
        await db.SubscriptionPlans.AddRangeAsync(currentPlan, targetPlan);
        await db.OperatorSubscriptions.AddAsync(subscription);
        await db.SubscriptionUpgradeAttempts.AddAsync(attempt);
        await db.SaveChangesAsync();

        return new PendingUpgradeSeed(
            operatorTenant.Id,
            subscription.Id,
            targetPlan.Id,
            attempt.Id,
            paymentId,
            targetPlan.PricePerMonth.Amount,
            now);
    }

    private static async Task ProcessSucceededAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        PendingUpgradeSeed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>();
        var handler = new SubscriptionPaymentSucceededIntegrationEventHandler(
            scope.ServiceProvider.GetRequiredService<SubscriptionPaymentActivationService>());
        var integrationEvent = new SubscriptionPaymentSucceededIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            PaymentId = seed.PaymentId,
            UpgradeAttemptId = seed.AttemptId,
            OperatorId = seed.OperatorId,
            OperatorSubscriptionId = seed.SubscriptionId,
            PlanId = seed.TargetPlanId,
            Amount = seed.Amount,
            Method = SubscriptionPaymentMethod.VNPAY.ToString(),
            BillingPeriod = SubscriptionBillingPeriod.MONTHLY.ToString(),
            PeriodFrom = seed.Now,
            PeriodTo = seed.Now.AddMonths(1),
            SucceededAt = seed.Now,
        };

        var result = await inbox.ExecuteAsync(
            "identity.subscription-payment-succeeded",
            integrationEvent.EventId,
            "SUCCEEDED_PAYLOAD_HASH",
            cancellationToken => handler.HandleAsync(integrationEvent, cancellationToken),
            CancellationToken.None);

        result.Should().Be(IntegrationEventInboxResult.Processed);
    }

    private static async Task ProcessExpiredAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        PendingUpgradeSeed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>();
        var handler = new SubscriptionPaymentTerminalIntegrationEventHandler(
            scope.ServiceProvider.GetRequiredService<ISubscriptionUpgradeAttemptRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<SubscriptionPaymentTerminalIntegrationEventHandler>.Instance);
        var integrationEvent = new SubscriptionPaymentExpiredIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            PaymentId = seed.PaymentId,
            UpgradeAttemptId = seed.AttemptId,
            OperatorId = seed.OperatorId,
            OperatorSubscriptionId = seed.SubscriptionId,
        };

        var result = await inbox.ExecuteAsync(
            "identity.subscription-payment-expired",
            integrationEvent.EventId,
            "EXPIRED_PAYLOAD_HASH",
            cancellationToken => handler.HandleAsync(integrationEvent, cancellationToken),
            CancellationToken.None);

        result.Should().Be(IntegrationEventInboxResult.Processed);
    }

    private static async Task AssertSucceededAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        PendingUpgradeSeed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var subscription = await db.OperatorSubscriptions.SingleAsync(item => item.Id == seed.SubscriptionId);
        var attempt = await db.SubscriptionUpgradeAttempts.SingleAsync(item => item.Id == seed.AttemptId);
        var inboxRecord = await db.Set<IntegrationInboxRecord>().SingleAsync(item =>
            item.ConsumerName == "identity.subscription-payment-succeeded");

        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.PlanId.Should().Be(seed.TargetPlanId);
        attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.SUCCEEDED);
        attempt.PaymentMethod.Should().Be(SubscriptionPaymentMethod.VNPAY);
        attempt.LatestPaymentStatus.Should().Be(SubscriptionPaymentSessionStatus.SUCCEEDED);
        inboxRecord.MessageId.Should().NotBeEmpty();
    }

    private static async Task AssertExpiredAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        PendingUpgradeSeed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var attempt = await db.SubscriptionUpgradeAttempts.SingleAsync(item => item.Id == seed.AttemptId);
        var inboxRecord = await db.Set<IntegrationInboxRecord>().SingleAsync(item =>
            item.ConsumerName == "identity.subscription-payment-expired");

        attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING);
        attempt.LatestPaymentStatus.Should().Be(SubscriptionPaymentSessionStatus.EXPIRED);
        inboxRecord.MessageId.Should().NotBeEmpty();
    }

    private sealed record PendingUpgradeSeed(
        Guid OperatorId,
        Guid SubscriptionId,
        Guid TargetPlanId,
        Guid AttemptId,
        Guid PaymentId,
        long Amount,
        DateTimeOffset Now);
}
