using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VietRide.Identity.Api.Filters;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Application.Features.Subscriptions.ConfirmSubscriptionUpgradePayment;
using VietRide.Identity.Application.Features.Subscriptions.CustomRequests;
using VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.IntegrationTests.Api;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.IntegrationTests;

public sealed class SubscriptionUpgradePostgresTests
{
    [Fact]
    public async Task CustomRequestTransitions_PersistBusinessActivityAndPendingOutboxAtomically()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            var callerUserId = seed.CallerUserId;
            var created = await SendAsync(
                factory,
                new CreateSubscriptionCustomRequestCommand(
                    callerUserId,
                    seed.OperatorId,
                    20,
                    20,
                    20,
                    20,
                    20,
                    1_000,
                    true,
                    true,
                    true,
                    "MONTHLY",
                    "Cần gói riêng"));
            var approved = await SendAsync(
                factory,
                new ApproveSubscriptionCustomRequestCommand(
                    callerUserId,
                    created.RequestId,
                    "Doanh nghiệp riêng",
                    null,
                    1_000_000,
                    10_000_000,
                    20,
                    20,
                    20,
                    20,
                    20,
                    1_000,
                    true,
                    true,
                    true));

            var rejectedRequest = await SendAsync(
                factory,
                new CreateSubscriptionCustomRequestCommand(
                    callerUserId,
                    seed.OperatorId,
                    30,
                    30,
                    30,
                    30,
                    30,
                    2_000,
                    true,
                    false,
                    true,
                    "YEARLY",
                    null));
            await SendAsync(
                factory,
                new RejectSubscriptionCustomRequestCommand(
                    callerUserId,
                    rejectedRequest.RequestId,
                    "Hạn mức chưa phù hợp"));

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var persistedRequests = await db.SubscriptionCustomRequests.AsNoTracking()
                .OrderBy(item => item.CreatedAt)
                .ToListAsync();
            persistedRequests.Should().Contain(item =>
                item.Id == created.RequestId
                && item.Status == SubscriptionCustomRequestStatus.APPROVED
                && item.ApprovedPlanId == approved.ApprovedPlanId);
            persistedRequests.Should().Contain(item =>
                item.Id == rejectedRequest.RequestId
                && item.Status == SubscriptionCustomRequestStatus.REJECTED
                && item.RejectionReason == "Hạn mức chưa phù hợp");

            var outboxEvents = await db.Set<OutboxEvent>().AsNoTracking()
                .Where(item => item.EventType.StartsWith("identity.subscription_custom_request."))
                .ToListAsync();
            outboxEvents.Should().HaveCount(4);
            outboxEvents.Should().OnlyContain(item => item.Status == OutboxEventStatus.PENDING);
            AssertOutboxIdentity(outboxEvents, SubscriptionCustomRequestSubmittedIntegrationEvent.EventType);
            AssertOutboxIdentity(outboxEvents, SubscriptionCustomRequestApprovedIntegrationEvent.EventType);
            AssertOutboxIdentity(outboxEvents, SubscriptionCustomRequestRejectedIntegrationEvent.EventType);
            (await db.ActivityLogs.AsNoTracking().CountAsync(item =>
                item.Action == ActivityLogAction.CREATE_SUBSCRIPTION_CUSTOM_REQUEST
                || item.Action == ActivityLogAction.APPROVE_SUBSCRIPTION_CUSTOM_REQUEST
                || item.Action == ActivityLogAction.REJECT_SUBSCRIPTION_CUSTOM_REQUEST))
                .Should().Be(4);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task CustomRequestSubmitted_WhenOutboxFails_RollsBackRequestAndActivityLog()
    {
        using var factory = new FailingOutboxSubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);

            var action = () => SendAsync(
                factory,
                new CreateSubscriptionCustomRequestCommand(
                    seed.CallerUserId,
                    seed.OperatorId,
                    20,
                    20,
                    20,
                    20,
                    20,
                    1_000,
                    true,
                    true,
                    true,
                    "MONTHLY",
                    null));

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("OUTBOX_UNAVAILABLE");

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.SubscriptionCustomRequests.AsNoTracking().CountAsync()).Should().Be(0);
            (await db.ActivityLogs.AsNoTracking().CountAsync(item =>
                item.Action == ActivityLogAction.CREATE_SUBSCRIPTION_CUSTOM_REQUEST)).Should().Be(0);
            (await db.Set<OutboxEvent>().AsNoTracking().CountAsync()).Should().Be(0);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ConcurrentConfirm_IsSerializedAndCallsPaymentOnlyOnce()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            var quote = await SendAsync(
                factory,
                new QuoteSubscriptionUpgradeCommand(
                    seed.OperatorId,
                    seed.TargetPlanId,
                    "MONTHLY",
                    "VNPAY",
                    Guid.NewGuid().ToString()));

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = ConfirmAsync(factory, gate.Task, seed.OperatorId, quote.UpgradeAttemptId);
            var second = ConfirmAsync(factory, gate.Task, seed.OperatorId, quote.UpgradeAttemptId);
            gate.SetResult();
            var outcomes = await Task.WhenAll(first, second);

            outcomes.Count(outcome => outcome.Exception is null).Should().Be(1);
            outcomes.Count(outcome => outcome.Exception is CodedConflictException).Should().Be(1);
            factory.Payments.CreateCalls.Should().Be(1);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var attempt = await db.SubscriptionUpgradeAttempts.AsNoTracking()
                .SingleAsync(item => item.Id == quote.UpgradeAttemptId);
            attempt.Status.Should().Be(SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING);
            attempt.PaymentId.Should().Be(factory.Payments.PaymentId);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task TargetDeactivatedAfterQuote_ConfirmRejectsBeforeCallingPayment()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            var quote = await SendAsync(
                factory,
                new QuoteSubscriptionUpgradeCommand(
                    seed.OperatorId,
                    seed.TargetPlanId,
                    "MONTHLY",
                    "WALLET",
                    Guid.NewGuid().ToString()));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var target = await db.SubscriptionPlans.SingleAsync(plan => plan.Id == seed.TargetPlanId);
                target.Deactivate();
                await db.SaveChangesAsync();
            }

            var action = () => SendAsync(
                factory,
                new ConfirmSubscriptionUpgradePaymentCommand(
                    seed.OperatorId,
                    quote.UpgradeAttemptId,
                    Guid.NewGuid().ToString(),
                    "203.0.113.10"));

            var exception = await action.Should().ThrowAsync<CodedConflictException>();
            exception.Which.ErrorCode.Should().Be("SUBSCRIPTION_UPGRADE_TARGET_PLAN_INACTIVE");
            factory.Payments.CreateCalls.Should().Be(0);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task PendingTargetCap_IsRemovedAfterLatestPaymentFails()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory, sourceLimit: 20, targetLimit: 5, currentVehicles: 4);
            var quote = await SendAsync(
                factory,
                new QuoteSubscriptionUpgradeCommand(
                    seed.OperatorId,
                    seed.TargetPlanId,
                    "MONTHLY",
                    "VNPAY",
                    Guid.NewGuid().ToString()));
            await SendAsync(
                factory,
                new ConfirmSubscriptionUpgradePaymentCommand(
                    seed.OperatorId,
                    quote.UpgradeAttemptId,
                    Guid.NewGuid().ToString(),
                    "203.0.113.10"));

            await using var scope = factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOperatorSubscriptionRepository>();
            var capped = await repository.TryIncrementUsageWithinLimitAsync(
                seed.OperatorId,
                SubscriptionUsageResource.VEHICLES,
                2,
                DateTimeOffset.UtcNow);
            capped.Should().BeNull();

            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var attempt = await db.SubscriptionUpgradeAttempts.SingleAsync(item => item.Id == quote.UpgradeAttemptId);
            attempt.MarkPaymentFailed(factory.Payments.PaymentId);
            await db.SaveChangesAsync();

            var afterFailure = await repository.TryIncrementUsageWithinLimitAsync(
                seed.OperatorId,
                SubscriptionUsageResource.VEHICLES,
                2,
                DateTimeOffset.UtcNow);
            afterFailure.Should().NotBeNull();
            afterFailure!.Value.Subscription.CurrentVehicles.Should().Be(6);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task AtomicUsageIncrement_AtExactExpiry_IsRejected()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var subscription = await db.OperatorSubscriptions.AsNoTracking()
                .SingleAsync(item => item.Id == seed.SubscriptionId);
            var repository = scope.ServiceProvider.GetRequiredService<IOperatorSubscriptionRepository>();

            var result = await repository.TryIncrementUsageWithinLimitAsync(
                seed.OperatorId,
                SubscriptionUsageResource.VEHICLES,
                1,
                subscription.ExpiresAt!.Value);

            result.Should().BeNull();
            (await db.OperatorSubscriptions.AsNoTracking()
                .SingleAsync(item => item.Id == seed.SubscriptionId))
                .CurrentVehicles.Should().Be(0);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ForeignCustomPlanAndRequest_AreMaskedAsNotFound()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            Guid customPlanId;
            Guid customRequestId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var owner = Operator.CreatePending(
                    $"Custom Owner {Guid.NewGuid():N}",
                    $"BR-{Guid.NewGuid():N}",
                    $"TAX-{Guid.NewGuid():N}",
                    $"custom-owner-{Guid.NewGuid():N}@example.test",
                    "+84901234568");
                var customRequest = SubscriptionCustomRequest.Create(
                    owner.Id,
                    50,
                    50,
                    50,
                    50,
                    50,
                    5_000,
                    true,
                    true,
                    true,
                    SubscriptionBillingPeriod.MONTHLY,
                    null);
                var customPlan = SubscriptionPlan.CreateCustom(
                    owner.Id,
                    customRequest.Id,
                    "Foreign Private Plan",
                    null,
                    Money.FromRaw(2_000_000),
                    Money.FromRaw(20_000_000),
                    50,
                    50,
                    50,
                    50,
                    50,
                    5_000,
                    true,
                    true,
                    true);
                db.Operators.Add(owner);
                db.SubscriptionCustomRequests.Add(customRequest);
                db.SubscriptionPlans.Add(customPlan);
                await db.SaveChangesAsync();
                customPlanId = customPlan.Id;
                customRequestId = customRequest.Id;
            }

            var quote = () => SendAsync(
                factory,
                new QuoteSubscriptionUpgradeCommand(
                    seed.OperatorId,
                    customPlanId,
                    "MONTHLY",
                    "WALLET",
                    Guid.NewGuid().ToString()));
            var request = () => SendAsync(
                factory,
                new GetSubscriptionCustomRequestQuery(customRequestId, seed.OperatorId));

            await quote.Should().ThrowAsync<NotFoundException>();
            await request.Should().ThrowAsync<NotFoundException>();
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ActiveAttemptUniqueViolation_UsesExactConstraintAndMapsTo409()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            var seed = await SeedAsync(factory);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.SubscriptionUpgradeAttempts.AddRange(
                CreateAttempt(seed, now, $"attempt-{Guid.NewGuid():N}"),
                CreateAttempt(seed, now, $"attempt-{Guid.NewGuid():N}"));

            var save = () => db.SaveChangesAsync();
            var exception = await save.Should().ThrowAsync<DbUpdateException>();
            var postgres = exception.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
            postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            postgres.ConstraintName.Should().Be("uq_subscription_upgrade_attempts_active_subscription");

            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary());
            var filterContext = new ExceptionContext(actionContext, [])
            {
                Exception = exception.Which,
            };

            new SubscriptionUniqueConstraintExceptionFilter().OnException(filterContext);

            filterContext.ExceptionHandled.Should().BeTrue();
            filterContext.Result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ExpandContractMigrations_DownAndReapplyWithZeroNullGate()
    {
        using var factory = new SubscriptionFactory();
        try
        {
            await factory.InitializeAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var migrator = db.GetService<IMigrator>();

            await migrator.MigrateAsync("20260819124524_ExpandSubscriptionProrationAndCustomPlans");
            (await CountNullableSnapshotColumnsAsync(db)).Should().Be(10);

            await migrator.MigrateAsync("20260813194833_AddUserSearchUnaccent");
            (await CountSnapshotColumnsAsync(db)).Should().Be(0);

            await migrator.MigrateAsync("20260819124524_ExpandSubscriptionProrationAndCustomPlans");
            (await CountNullableSnapshotColumnsAsync(db)).Should().Be(10);

            await migrator.MigrateAsync();
            (await CountNullableSnapshotColumnsAsync(db)).Should().Be(0);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    private static SubscriptionUpgradeAttempt CreateAttempt(SubscriptionSeed seed, DateTimeOffset now, string key)
        => SubscriptionUpgradeAttempt.CreateQuote(
            seed.SubscriptionId,
            seed.OperatorId,
            seed.SourcePlanId,
            seed.TargetPlanId,
            SubscriptionBillingPeriod.MONTHLY,
            Money.FromRaw(500_000),
            SubscriptionPaymentMethod.WALLET,
            key,
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            now,
            now.AddMinutes(15),
            now,
            now.AddMonths(1),
            Money.Zero,
            Money.FromRaw(500_000),
            Money.Zero,
            Money.FromRaw(500_000),
            false);

    private static void AssertOutboxIdentity(IReadOnlyCollection<OutboxEvent> events, string eventType)
    {
        foreach (var outboxEvent in events.Where(item => item.EventType == eventType))
        {
            using var document = JsonDocument.Parse(outboxEvent.Payload);
            document.RootElement.GetProperty("eventId").GetGuid().Should().Be(outboxEvent.Id);
            document.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset
                .Should().Be(TimeSpan.Zero);
        }
    }

    private static async Task<SubscriptionSeed> SeedAsync(
        SubscriptionFactory factory,
        int sourceLimit = 5,
        int targetLimit = 20,
        int currentVehicles = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        var operatorTenant = Operator.CreatePending(
            $"Subscription Test {Guid.NewGuid():N}",
            $"BR-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"subscription-{Guid.NewGuid():N}@example.test",
            "+84901234567");
        var source = SubscriptionPlan.Create(
            $"Source {Guid.NewGuid():N}",
            null,
            Money.FromRaw(100_000),
            Money.FromRaw(1_000_000),
            sourceLimit,
            sourceLimit,
            sourceLimit,
            sourceLimit,
            sourceLimit,
            100,
            false,
            false,
            true);
        var target = SubscriptionPlan.Create(
            $"Target {Guid.NewGuid():N}",
            null,
            Money.FromRaw(500_000),
            Money.FromRaw(5_000_000),
            targetLimit,
            targetLimit,
            targetLimit,
            targetLimit,
            targetLimit,
            1_000,
            true,
            true,
            true);
        var subscription = OperatorSubscription.CreateActiveTrial(
            operatorTenant.Id,
            source.Id,
            now.AddDays(-1),
            now.AddDays(29));
        if (currentVehicles > 0)
            subscription.IncrementUsage(SubscriptionUsageResource.VEHICLES, currentVehicles);
        var caller = User.CreateAdminPendingPassword(
            $"subscription-admin-{Guid.NewGuid():N}@example.test",
            "Subscription Admin");
        db.Operators.Add(operatorTenant);
        db.Users.Add(caller);
        db.SubscriptionPlans.AddRange(source, target);
        db.OperatorSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return new SubscriptionSeed(
            operatorTenant.Id,
            subscription.Id,
            source.Id,
            target.Id,
            caller.Id);
    }

    private static async Task<T> SendAsync<T>(SubscriptionFactory factory, IRequest<T> request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private static async Task<ConfirmOutcome> ConfirmAsync(
        SubscriptionFactory factory,
        Task gate,
        Guid operatorId,
        Guid attemptId)
    {
        await gate;
        try
        {
            var response = await SendAsync(
                factory,
                new ConfirmSubscriptionUpgradePaymentCommand(
                    operatorId,
                    attemptId,
                    Guid.NewGuid().ToString(),
                    "203.0.113.10"));
            return new ConfirmOutcome(response, null);
        }
        catch (Exception exception)
        {
            return new ConfirmOutcome(null, exception);
        }
    }

    private static Task<int> CountSnapshotColumnsAsync(IdentityDbContext db)
        => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'vietride_identity'
              AND ((table_name = 'operator_subscriptions' AND column_name = 'cycle_price_amount')
                OR (table_name = 'subscription_upgrade_attempts' AND column_name IN (
                    'source_plan_id', 'quoted_at', 'period_from', 'period_to',
                    'current_cycle_price_amount', 'target_cycle_price_amount',
                    'unused_credit_amount', 'prorated_target_amount', 'is_prorated')))
            """).SingleAsync();

    private static Task<int> CountNullableSnapshotColumnsAsync(IdentityDbContext db)
        => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'vietride_identity'
              AND is_nullable = 'YES'
              AND ((table_name = 'operator_subscriptions' AND column_name = 'cycle_price_amount')
                OR (table_name = 'subscription_upgrade_attempts' AND column_name IN (
                    'source_plan_id', 'quoted_at', 'period_from', 'period_to',
                    'current_cycle_price_amount', 'target_cycle_price_amount',
                    'unused_credit_amount', 'prorated_target_amount', 'is_prorated')))
            """).SingleAsync();

    private sealed record SubscriptionSeed(
        Guid OperatorId,
        Guid SubscriptionId,
        Guid SourcePlanId,
        Guid TargetPlanId,
        Guid CallerUserId);

    private sealed record ConfirmOutcome(SubscriptionUpgradeResponseDto? Response, Exception? Exception);

    private class SubscriptionFactory : AdminUsersEndpointsTests.DbBackedAdminUsersFactory
    {
        public FakeSubscriptionPaymentClient Payments { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionPaymentClient>();
                services.AddSingleton<ISubscriptionPaymentClient>(Payments);
            });
        }
    }

    private sealed class FailingOutboxSubscriptionFactory : SubscriptionFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIntegrationEventOutbox>();
                services.AddSingleton<IIntegrationEventOutbox, FailingIntegrationEventOutbox>();
            });
        }
    }

    private sealed class FailingIntegrationEventOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
            => throw new InvalidOperationException("OUTBOX_UNAVAILABLE");

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
            => throw new InvalidOperationException("OUTBOX_UNAVAILABLE");
    }

    private sealed class FakeSubscriptionPaymentClient : ISubscriptionPaymentClient
    {
        private int _createCalls;

        public Guid PaymentId { get; } = Guid.NewGuid();
        public int CreateCalls => Volatile.Read(ref _createCalls);

        public async Task<SubscriptionPaymentCreationResult> CreateAsync(
            SubscriptionPaymentCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCalls);
            await Task.Delay(100, cancellationToken);
            return new SubscriptionPaymentCreationResult(
                PaymentId,
                "PENDING_REDIRECT",
                "https://sandbox.vnpay.test/pay",
                null);
        }

        public Task ExpireAsync(
            Guid paymentId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SubscriptionPaymentStatusResult>> GetStatusesAsync(
            IReadOnlyCollection<Guid> upgradeAttemptIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SubscriptionPaymentStatusResult>>([]);
    }
}
