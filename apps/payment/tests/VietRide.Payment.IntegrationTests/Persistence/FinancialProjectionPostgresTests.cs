using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Payment.IntegrationTests.Persistence;

public sealed class FinancialProjectionPostgresTests
{
    [Fact]
    public async Task ManualSettlement_PersistsActorAcrossSettlementAndPlatformLedger()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_FINANCIAL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var identity = new FakeFinancialIdentityClient(
            new IdentityFinancialOperator(operatorId, "Operator A", null, "+84901234567"),
            new IdentityFinancialUser(adminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN", false));
        await using var provider = CreateProvider(connectionString, identity);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var platformWallet = await db.PlatformWallets.SingleOrDefaultAsync();
        if (platformWallet is null)
        {
            platformWallet = PlatformWallet.Create();
            db.PlatformWallets.Add(platformWallet);
        }
        platformWallet.Credit(Money.FromRaw(1_000_000));

        var terminalAt = DateTimeOffset.UtcNow.AddDays(-8);
        var settlement = OperatorTripSettlement.CreatePending(operatorId, tripId, terminalAt);
        settlement.RefreshEligibility(500_000, DateTimeOffset.UtcNow);
        var sourceId = Guid.NewGuid();
        db.OperatorTripSettlements.Add(settlement);
        db.OperatorLedgerEntries.Add(OperatorLedgerEntry.Create(
            operatorId,
            tripId,
            OperatorLedgerEntryType.BOOKING_REVENUE,
            500_000,
            OperatorLedgerReferenceType.BOOKING,
            Guid.NewGuid(),
            sourceId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = scope.ServiceProvider.GetRequiredService<IFinancialManagementService>();
        await service.SettleAsync(settlement.Id, adminId, CancellationToken.None);

        db.ChangeTracker.Clear();
        var persisted = await db.OperatorTripSettlements.SingleAsync(item => item.Id == settlement.Id);
        persisted.Status.Should().Be(OperatorTripSettlementStatus.SETTLED);
        persisted.OperatorSnapshotResolved.Should().BeFalse();
        persisted.OperatorName.Should().BeNull();
        persisted.SettledBySnapshotResolved.Should().BeTrue();
        persisted.SettledByUserId.Should().Be(adminId);
        persisted.SettledByDisplayName.Should().Be("System Admin");
        persisted.SettledByEmail.Should().Be("admin@vietride.vn");
        persisted.SettledByRole.Should().Be("SYSTEM_ADMIN");

        var movement = await db.PlatformWalletTransactions.SingleAsync(item =>
            item.ReferenceType == PlatformWalletTransactionRef.TRIP_SETTLEMENT
            && item.ReferenceId == settlement.Id);
        movement.ActorType.Should().Be(FinancialActorType.USER);
        movement.ActorUserId.Should().Be(adminId);
        movement.ActorDisplayName.Should().Be("System Admin");
        movement.ActorSnapshotResolved.Should().BeTrue();
        var link = await db.PlatformWalletTransactionLinks.SingleAsync(item =>
            item.PlatformWalletTransactionId == movement.Id);
        link.LinkType.Should().Be(PlatformWalletTransactionLinkType.TRIP_SETTLEMENT);
        link.OperatorId.Should().Be(operatorId);
        link.TripId.Should().Be(tripId);
        link.ReferenceId.Should().Be(settlement.Id);
        link.ReferenceCode.Should().Be(settlement.SettlementCode);
        link.AllocatedAmount.Should().Be(500_000);
        var operatorMovement = await db.OperatorWalletTransactions.SingleAsync(item =>
            item.ReferenceType == OperatorWalletTransactionRef.TRIP_SETTLEMENT
            && item.ReferenceId == settlement.Id);
        operatorMovement.Amount.Amount.Should().Be(movement.Amount.Amount);

        var adjustment = await service.AdjustPlatformWalletAsync(
            new AdjustmentRequest("CREDIT", 10_000, "manual correction"),
            adminId,
            CancellationToken.None);
        db.ChangeTracker.Clear();
        var adjustmentMovement = await db.PlatformWalletTransactions
            .SingleAsync(item => item.Id == adjustment.TransactionId);
        adjustmentMovement.ReferenceType.Should().Be(PlatformWalletTransactionRef.MANUAL_ADJUSTMENT);
        adjustmentMovement.ActorType.Should().Be(FinancialActorType.USER);
        adjustmentMovement.ActorUserId.Should().Be(adminId);
        adjustmentMovement.ActorEmail.Should().Be("admin@vietride.vn");
        adjustmentMovement.ActorSnapshotResolved.Should().BeTrue();

        var page = await service.ListAdminSettlementsAsync(
            new PageOptions(PageSize: 100), operatorId, "SETTLED", tripId, false, null,
            CancellationToken.None);
        page.Items.Should().ContainSingle();
        page.Items[0].Operator.Should().Be(
            new FinancialOperatorDto(operatorId, "Operator A", null, "+84901234567"));
        page.Items[0].SettledBy.Should().Be(
            new FinancialActorDto(adminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN"));
        identity.OperatorCalls.Should().Be(1);
        identity.UserCalls.Should().Be(2);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_payment.operator_trip_settlements
            SET updated_at = CURRENT_TIMESTAMP - INTERVAL '1 day'
            WHERE id = {settlement.Id};
            """);
        db.ChangeTracker.Clear();
        var updatedAtBeforeRedaction = (await db.OperatorTripSettlements
            .AsNoTracking()
            .SingleAsync(item => item.Id == settlement.Id)).UpdatedAt;
        var deletedEvent = new IdentityUserDeletedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            UserId = adminId,
        };
        var inbox = scope.ServiceProvider.GetRequiredService<IIntegrationEventInbox>();
        var handler = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventHandler<IdentityUserDeletedIntegrationEvent>>();
        var inboxResult = await inbox.ExecuteAsync(
            "payment.identity-user-deleted",
            deletedEvent.EventId,
            "ui05-identity-user-deleted",
            ct => handler.HandleAsync(deletedEvent, ct),
            CancellationToken.None);
        inboxResult.Should().Be(IntegrationEventInboxResult.Processed);

        var privacy = scope.ServiceProvider.GetRequiredService<IFinancialActorPrivacyStore>();
        (await privacy.MarkDeletedAndRedactAsync(adminId)).Should().Be(0);
        db.ChangeTracker.Clear();
        var redactedSettlement = await db.OperatorTripSettlements
            .AsNoTracking()
            .SingleAsync(item => item.Id == settlement.Id);
        redactedSettlement.SettledByDisplayName.Should().Be("Người dùng đã xóa");
        redactedSettlement.SettledByEmail.Should().BeNull();
        redactedSettlement.SettledByRole.Should().BeNull();
        redactedSettlement.UpdatedAt.Should().BeAfter(updatedAtBeforeRedaction);
        var redactedMovements = await db.PlatformWalletTransactions
            .AsNoTracking()
            .Where(item => item.Id == movement.Id || item.Id == adjustmentMovement.Id)
            .ToArrayAsync();
        redactedMovements.Should().OnlyContain(item =>
            item.ActorDisplayName == "Người dùng đã xóa"
            && item.ActorEmail == null
            && item.ActorRole == null
            && item.ActorSnapshotResolved);
        (await db.DeletedFinancialActorMarkers.AsNoTracking().AnyAsync(item => item.UserId == adminId))
            .Should().BeTrue();

        var movementCount = await db.PlatformWalletTransactions.LongCountAsync();
        var deletedWrite = () => service.AdjustPlatformWalletAsync(
            new AdjustmentRequest("CREDIT", 10_000, "must not persist"),
            adminId,
            CancellationToken.None);
        await deletedWrite.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.PlatformWalletTransactions.LongCountAsync()).Should().Be(movementCount);
    }

    [Fact]
    public async Task ManualCancellation_PersistsAuthenticatedActorSnapshot()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_FINANCIAL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var operatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var identity = new FakeFinancialIdentityClient(
            new IdentityFinancialOperator(operatorId, "Operator A", null, null),
            new IdentityFinancialUser(adminId, "System Admin", "admin@vietride.vn", "SYSTEM_ADMIN", false));
        await using var provider = CreateProvider(connectionString, identity);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var settlement = OperatorTripSettlement.CreatePending(
            operatorId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-8));
        db.OperatorTripSettlements.Add(settlement);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = scope.ServiceProvider.GetRequiredService<IFinancialManagementService>();
        await service.SettleAsync(settlement.Id, adminId, CancellationToken.None);

        db.ChangeTracker.Clear();
        var persisted = await db.OperatorTripSettlements.AsNoTracking()
            .SingleAsync(item => item.Id == settlement.Id);
        persisted.Status.Should().Be(OperatorTripSettlementStatus.CANCELLED);
        persisted.SettledByUserId.Should().Be(adminId);
        persisted.SettledByDisplayName.Should().Be("System Admin");
        persisted.SettledByEmail.Should().Be("admin@vietride.vn");
        persisted.SettledByRole.Should().Be("SYSTEM_ADMIN");
        persisted.SettledBySnapshotResolved.Should().BeTrue();
    }

    [Fact]
    public async Task Backfill_UsesSingleBoundedBatchAndMarksMissingSnapshotsResolved()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_FINANCIAL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var foundOperatorId = Guid.NewGuid();
        var missingOperatorId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var identity = new FakeFinancialIdentityClient(
            new IdentityFinancialOperator(foundOperatorId, "Operator A", null, "+84901234567"),
            new IdentityFinancialUser(actorUserId, "Legacy Admin", "legacy-admin@vietride.vn", "SYSTEM_ADMIN", false));
        await using var provider = CreateProvider(connectionString, identity);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var first = OperatorTripSettlement.CreatePending(
            foundOperatorId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var second = OperatorTripSettlement.CreatePending(
            missingOperatorId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.OperatorTripSettlements.AddRange(first, second);
        var legacyTransaction = PlatformWalletTransaction.Create(
            PlatformWalletTransactionType.DEBIT,
            Money.FromRaw(10_000),
            Money.FromRaw(100_000),
            Money.FromRaw(90_000),
            PlatformWalletTransactionRef.MANUAL_ADJUSTMENT,
            note: "legacy manual adjustment");
        db.PlatformWalletTransactions.Add(legacyTransaction);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vietride_payment.platform_wallet_transactions SET actor_type='USER', actor_user_id={actorUserId}, actor_snapshot_resolved=FALSE WHERE id={legacyTransaction.Id}");
        db.ChangeTracker.Clear();

        var service = scope.ServiceProvider.GetRequiredService<IFinancialManagementService>();
        var fallbackPage = await service.ListPlatformTransactionsAsync(
            new PageOptions(PageSize: 100), null, "MANUAL_ADJUSTMENT", CancellationToken.None);
        fallbackPage.Items.Single(item => item.TransactionId == legacyTransaction.Id).Actor.Should().Be(
            new FinancialActorDto(actorUserId, "Legacy Admin", "legacy-admin@vietride.vn", "SYSTEM_ADMIN"));

        var job = scope.ServiceProvider.GetRequiredService<FinancialProjectionBackfillJob>();
        await job.RunAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var rows = await db.OperatorTripSettlements
            .Where(item => item.Id == first.Id || item.Id == second.Id)
            .OrderBy(item => item.Id)
            .ToArrayAsync();
        rows.Should().OnlyContain(item => item.OperatorSnapshotResolved);
        rows.Single(item => item.OperatorId == foundOperatorId).OperatorName.Should().Be("Operator A");
        rows.Single(item => item.OperatorId == missingOperatorId).OperatorName.Should().BeNull();
        var persistedTransaction = await db.PlatformWalletTransactions
            .SingleAsync(item => item.Id == legacyTransaction.Id);
        persistedTransaction.ActorSnapshotResolved.Should().BeTrue();
        persistedTransaction.ActorDisplayName.Should().Be("Legacy Admin");
        persistedTransaction.ActorEmail.Should().Be("legacy-admin@vietride.vn");
        identity.OperatorCalls.Should().Be(1);
        identity.MaxOperatorBatchSize.Should().BeInRange(2, FinancialProjectionBackfillJob.BatchSize);
        identity.UserCalls.Should().BeInRange(
            2,
            3,
            "one read fallback plus at most one settlement-user and one platform-user batch are expected");
        var callsAfterFirstBackfill = identity.UserCalls;

        await job.RunAsync(CancellationToken.None);
        identity.OperatorCalls.Should().Be(1, "resolved missing rows must not starve later batches");
        identity.UserCalls.Should().Be(callsAfterFirstBackfill, "resolved actors must not be selected again");
    }

    [Fact]
    public async Task Backfill_WhenDeletionCommitsDuringIdentityLookup_CannotRestorePii()
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_FINANCIAL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var actorUserId = Guid.NewGuid();
        var identity = new FakeFinancialIdentityClient(
            @operator: null,
            new IdentityFinancialUser(actorUserId, "Must Not Reappear", "leaked@vietride.vn", "SYSTEM_ADMIN", false));
        await using var provider = CreateProvider(connectionString, identity);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var transaction = PlatformWalletTransaction.Create(
            PlatformWalletTransactionType.CREDIT,
            Money.FromRaw(10_000),
            Money.FromRaw(0),
            Money.FromRaw(10_000),
            PlatformWalletTransactionRef.MANUAL_ADJUSTMENT,
            note: "race fixture");
        db.PlatformWalletTransactions.Add(transaction);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE vietride_payment.platform_wallet_transactions SET actor_type='USER', actor_user_id={actorUserId}, actor_snapshot_resolved=FALSE WHERE id={transaction.Id}");
        db.ChangeTracker.Clear();
        identity.BeforeReturningUsersAsync = async () =>
        {
            await using var redactionScope = provider.CreateAsyncScope();
            var privacy = redactionScope.ServiceProvider.GetRequiredService<IFinancialActorPrivacyStore>();
            await privacy.MarkDeletedAndRedactAsync(actorUserId);
        };

        var job = scope.ServiceProvider.GetRequiredService<FinancialProjectionBackfillJob>();
        await job.RunAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var persisted = await db.PlatformWalletTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == transaction.Id);
        persisted.ActorDisplayName.Should().Be("Người dùng đã xóa");
        persisted.ActorEmail.Should().BeNull();
        persisted.ActorRole.Should().BeNull();
        persisted.ActorSnapshotResolved.Should().BeTrue();
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        IIdentityFinancialProjectionClient identity)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        PaymentDbContext.ConfigurePostgresTypes(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InvoiceStorage:Provider"] = "E2E_LOCAL",
                ["Identity:BaseUrl"] = "http://identity.test",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(dataSource));
        services.AddScoped<VietRideDbContextBase>(provider =>
            provider.GetRequiredService<PaymentDbContext>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IIntegrationEventOutbox, CapturingOutbox>();
        services.AddInfrastructure(configuration, registerConsumers: false);
        services.AddScoped<
            IIntegrationEventHandler<IdentityUserDeletedIntegrationEvent>,
            IdentityUserDeletedIntegrationEventHandler>();
        services.AddScoped<FinancialProjectionBackfillJob>();
        services.RemoveAll<IIdentityFinancialProjectionClient>();
        services.AddSingleton(identity);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingOutbox : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFinancialIdentityClient : IIdentityFinancialProjectionClient
    {
        private readonly IdentityFinancialOperator? _operator;
        private readonly IdentityFinancialUser? _user;
        private int _beforeReturningUsersInvoked;

        public FakeFinancialIdentityClient(
            IdentityFinancialOperator? @operator,
            IdentityFinancialUser? user)
        {
            _operator = @operator;
            _user = user;
        }

        public int OperatorCalls { get; private set; }
        public int UserCalls { get; private set; }
        public int MaxOperatorBatchSize { get; private set; }
        public Func<Task>? BeforeReturningUsersAsync { get; set; }

        public Task<IReadOnlyList<IdentityFinancialOperator>> GetOperatorsAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
        {
            OperatorCalls++;
            MaxOperatorBatchSize = Math.Max(MaxOperatorBatchSize, operatorIds.Count);
            IReadOnlyList<IdentityFinancialOperator> result = _operator is not null
                && operatorIds.Contains(_operator.OperatorId)
                ? [_operator]
                : [];
            return Task.FromResult(result);
        }

        public async Task<IReadOnlyList<IdentityFinancialUser>> GetUsersAsync(
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            UserCalls++;
            if (BeforeReturningUsersAsync is not null
                && Interlocked.Exchange(ref _beforeReturningUsersInvoked, 1) == 0)
            {
                await BeforeReturningUsersAsync();
            }
            IReadOnlyList<IdentityFinancialUser> result = _user is not null
                && userIds.Contains(_user.UserId)
                ? [_user]
                : [];
            return result;
        }
    }
}
