using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Identity.IntegrationTests.Api;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.IntegrationTests;

public sealed class StationAuditConsumerTests
{
    private const string PreviousMigration = "20260716132910_AddImmutableActivityLogReadModel";

    [Fact]
    public async Task Consumers_AreReversibleIdempotentConcurrentSafeAndRejectInvalidActors()
    {
        using var factory = new AdminUsersEndpointsTests.DbBackedAdminUsersFactory();
        try
        {
            await factory.InitializeAsync();
            await AssertMigrationDownAndReapplyAsync(factory);
            var actorId = await SeedActorAsync(factory);
            var mergeEvent = CreateMergedEvent(actorId, "203.0.113.10", "VietRide Admin Web");
            var normalizedEvent = CreateNormalizedEvent(actorId);

            await HandleMergedAsync(factory, mergeEvent);
            await HandleMergedAsync(factory, mergeEvent);
            await HandleNormalizedAsync(factory, normalizedEvent);

            var concurrentEvent = CreateNormalizedEvent(actorId);
            await HandleConcurrentReplayAsync(factory, concurrentEvent);

            var missingActorEvent = CreateMergedEvent(Guid.NewGuid(), null, null);
            var missingActor = () => HandleMergedAsync(factory, missingActorEvent);
            await missingActor.Should().ThrowAsync<DbUpdateException>();

            var invalidPayloadEvent = CreateNormalizedEvent(actorId, invalidSnapshot: true);
            var invalidPayload = () => HandleNormalizedAsync(factory, invalidPayloadEvent);
            await invalidPayload.Should().ThrowAsync<InvalidOperationException>();

            await AssertPersistedLogsAsync(
                factory,
                actorId,
                mergeEvent,
                normalizedEvent,
                concurrentEvent,
                missingActorEvent,
                invalidPayloadEvent);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    private static async Task AssertMigrationDownAndReapplyAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var migrator = db.GetService<IMigrator>();
        (await ReadStationAuditLabelsAsync(db)).Should().BeEquivalentTo(
            ["STATION_MERGED", "STATION_NORMALIZED"]);

        await migrator.MigrateAsync(PreviousMigration);
        (await ReadStationAuditLabelsAsync(db)).Should().BeEmpty();

        await migrator.MigrateAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await connection.ReloadTypesAsync();
        (await ReadStationAuditLabelsAsync(db)).Should().BeEquivalentTo(
            ["STATION_MERGED", "STATION_NORMALIZED"]);
    }

    private static async Task<IReadOnlyList<string>> ReadStationAuditLabelsAsync(IdentityDbContext db)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT enumlabel
            FROM pg_enum enum_value
            JOIN pg_type enum_type ON enum_type.oid = enum_value.enumtypid
            WHERE enum_type.typname = 'activity_log_action'
              AND enumlabel IN ('STATION_MERGED', 'STATION_NORMALIZED')
            ORDER BY enumlabel;
            """;
        var labels = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            labels.Add(reader.GetString(0));
        return labels;
    }

    private static async Task<Guid> SeedActorAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var actor = User.CreatePassenger(
            $"station-audit-{Guid.NewGuid():N}@example.com",
            PhoneNumber.Parse("+84901234567"),
            "hash",
            "Station Admin");
        actor.VerifyEmail();
        await db.Users.AddAsync(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    private static async Task HandleMergedAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        StationMergedIntegrationEvent integrationEvent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new StationMergedIntegrationEventHandler(
            scope.ServiceProvider.GetRequiredService<IActivityLogRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<StationMergedIntegrationEventHandler>.Instance);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleNormalizedAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        StationNormalizedIntegrationEvent integrationEvent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new StationNormalizedIntegrationEventHandler(
            scope.ServiceProvider.GetRequiredService<IActivityLogRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<StationNormalizedIntegrationEventHandler>.Instance);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleConcurrentReplayAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        StationNormalizedIntegrationEvent integrationEvent)
    {
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var gate = new TwoPartyGate();
        var firstRepository = new GatedActivityLogRepository(
            firstScope.ServiceProvider.GetRequiredService<IActivityLogRepository>(),
            gate);
        var secondRepository = new GatedActivityLogRepository(
            secondScope.ServiceProvider.GetRequiredService<IActivityLogRepository>(),
            gate);
        var firstHandler = new StationNormalizedIntegrationEventHandler(
            firstRepository,
            firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<StationNormalizedIntegrationEventHandler>.Instance);
        var secondHandler = new StationNormalizedIntegrationEventHandler(
            secondRepository,
            secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            NullLogger<StationNormalizedIntegrationEventHandler>.Instance);

        await Task.WhenAll(
            firstHandler.HandleAsync(integrationEvent, CancellationToken.None),
            secondHandler.HandleAsync(integrationEvent, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task AssertPersistedLogsAsync(
        AdminUsersEndpointsTests.DbBackedAdminUsersFactory factory,
        Guid actorId,
        StationMergedIntegrationEvent mergeEvent,
        StationNormalizedIntegrationEvent normalizedEvent,
        StationNormalizedIntegrationEvent concurrentEvent,
        StationMergedIntegrationEvent missingActorEvent,
        StationNormalizedIntegrationEvent invalidPayloadEvent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logs = await db.ActivityLogs.AsNoTracking()
            .Where(log => log.SourceEventId == mergeEvent.EventId
                || log.SourceEventId == normalizedEvent.EventId
                || log.SourceEventId == concurrentEvent.EventId
                || log.SourceEventId == missingActorEvent.EventId
                || log.SourceEventId == invalidPayloadEvent.EventId)
            .ToListAsync();

        logs.Should().HaveCount(3);
        logs.Should().OnlyContain(log => log.UserId == actorId && log.Metadata == null);
        logs.Should().ContainSingle(log =>
            log.SourceEventId == mergeEvent.EventId
            && log.Action == ActivityLogAction.STATION_MERGED
            && log.IpAddress == mergeEvent.IpAddress
            && log.UserAgent == mergeEvent.UserAgent);
        logs.Should().ContainSingle(log =>
            log.SourceEventId == normalizedEvent.EventId
            && log.Action == ActivityLogAction.STATION_NORMALIZED);
        logs.Should().ContainSingle(log =>
            log.SourceEventId == concurrentEvent.EventId
            && log.Action == ActivityLogAction.STATION_NORMALIZED);
        logs.Should().NotContain(log =>
            log.SourceEventId == missingActorEvent.EventId
            || log.SourceEventId == invalidPayloadEvent.EventId);
    }

    private static StationMergedIntegrationEvent CreateMergedEvent(
        Guid actorUserId,
        string? ipAddress,
        string? userAgent)
    {
        var primaryStationId = Guid.NewGuid();
        var duplicateStationId = Guid.NewGuid();
        return new StationMergedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            ActorUserId = actorUserId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            PrimaryStationId = primaryStationId,
            DuplicateStationId = duplicateStationId,
            PrimaryBefore = CreateSnapshot(primaryStationId),
            DuplicateBefore = CreateSnapshot(duplicateStationId),
            PrimaryAfter = CreateSnapshot(primaryStationId),
            RelinkedCounts = new StationRelinkedCounts
            {
                OperatorMappings = 1,
                RouteDestinations = 1,
            },
        };
    }

    private static StationNormalizedIntegrationEvent CreateNormalizedEvent(
        Guid actorUserId,
        bool invalidSnapshot = false)
    {
        var stationId = Guid.NewGuid();
        var snapshotStationId = invalidSnapshot ? Guid.NewGuid() : stationId;
        return new StationNormalizedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            ActorUserId = actorUserId,
            StationId = stationId,
            Before = CreateSnapshot(snapshotStationId),
            After = CreateSnapshot(snapshotStationId),
        };
    }

    private static StationAuditSnapshot CreateSnapshot(Guid stationId)
        => new()
        {
            Id = stationId,
            Name = "Ben xe Mien Dong",
            Slug = "ben-xe-mien-dong",
            City = "Thu Duc",
            Province = "Ho Chi Minh",
            Latitude = 10.8796m,
            Longitude = 106.8142m,
            SupportsShuttle = true,
            IsActive = true,
        };

    private sealed class TwoPartyGate
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult();
            return _ready.Task;
        }
    }

    private sealed class GatedActivityLogRepository : IActivityLogRepository
    {
        private readonly IActivityLogRepository _inner;
        private readonly TwoPartyGate _gate;

        public GatedActivityLogRepository(IActivityLogRepository inner, TwoPartyGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => _inner.GetByIdAsync(id, ct);

        public Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct = default)
            => _inner.AddAsync(entity, ct);

        public async Task<bool> ExistsBySourceEventIdAsync(
            Guid sourceEventId,
            CancellationToken ct = default)
        {
            var exists = await _inner.ExistsBySourceEventIdAsync(sourceEventId, ct);
            if (!exists)
                await _gate.ArriveAsync().WaitAsync(ct);
            return exists;
        }
    }
}
