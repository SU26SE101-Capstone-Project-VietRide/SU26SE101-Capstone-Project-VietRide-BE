using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.AutoRejectPendingParcel;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;
using VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Messaging;

public sealed class Day29ParcelAutoRejectedProducerIntegrationTests
{
    private const string AutoRejectedRoutingKey = "parcel.parcel.auto_rejected";
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task LateLoadWinner_CommitsTransitionAndPendingAutoRejectedOutboxAtomically()
        => RunScenarioAsync(TimeoutSource.LateLoad, failCommit: false);

    [Fact]
    public Task AdditionalPaymentTimeoutWinner_CommitsTransitionRefundAndPendingAutoRejectedOutboxAtomically()
        => RunScenarioAsync(TimeoutSource.AdditionalPayment, failCommit: false);

    [Fact]
    public Task ReviewTimeoutWinner_CommitsTransitionAndPendingAutoRejectedOutboxAtomically()
        => RunScenarioAsync(TimeoutSource.Review, failCommit: false);

    [Fact]
    public Task LateLoadForcedFailure_RollsBackTransitionAndAutoRejectedOutbox()
        => RunScenarioAsync(TimeoutSource.LateLoad, failCommit: true);

    [Fact]
    public Task AdditionalPaymentTimeoutForcedFailure_RollsBackTransitionRefundAndAutoRejectedOutbox()
        => RunScenarioAsync(TimeoutSource.AdditionalPayment, failCommit: true);

    [Fact]
    public Task ReviewTimeoutForcedFailure_RollsBackTransitionAndAutoRejectedOutbox()
        => RunScenarioAsync(TimeoutSource.Review, failCommit: true);

    private static async Task RunScenarioAsync(TimeoutSource source, bool failCommit)
    {
        var databaseName = $"vietride_parcel_day29_auto_reject_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var senderUserId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var parcel = CreateParcel(source, senderUserId, operatorId, tripId);

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.Add(parcel);
                await seedContext.SaveChangesAsync();
                await SetTimeoutStateAsync(seedContext, parcel.Id, source);
            }

            await using (var handlerContext = CreateDbContext(dataSource))
            {
                var clock = new FrozenClock(Now);
                var repository = CreateRepository<IParcelRepository>(
                    "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
                    handlerContext);
                var stats = CreateRepository<IParcelStatsRepository>(
                    "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelStatsRepository",
                    handlerContext);
                var outbox = new IntegrationEventOutbox(new OutboxStore(handlerContext, clock));
                IUnitOfWork unitOfWork = new EfUnitOfWork(handlerContext);
                if (failCommit)
                {
                    unitOfWork = new CommitFailingUnitOfWork(unitOfWork);
                }

                var invocation = () => InvokeHandlerAsync(
                    source,
                    repository,
                    stats,
                    outbox,
                    unitOfWork,
                    clock,
                    operatorId,
                    tripId);

                if (failCommit)
                {
                    await invocation.Should().ThrowAsync<InvalidOperationException>()
                        .WithMessage("simulated commit failure");
                }
                else
                {
                    (await invocation()).Should().Be(1);
                }
            }

            await AssertPersistedStateAsync(
                dataSource,
                source,
                parcel.Id,
                parcel.ParcelCode,
                senderUserId,
                operatorId,
                tripId,
                failCommit);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task<int> InvokeHandlerAsync(
        TimeoutSource source,
        IParcelRepository repository,
        IParcelStatsRepository stats,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        Guid operatorId,
        Guid tripId)
    {
        var identity = new IdentityStub(operatorId);
        return source switch
        {
            TimeoutSource.LateLoad => await new AutoRejectPendingParcelCommandHandler(
                repository,
                new TripStub(CreateTripSnapshot(operatorId, tripId)),
                identity,
                clock,
                unitOfWork,
                outbox,
                NullLogger<AutoRejectPendingParcelCommandHandler>.Instance,
                stats).Handle(new AutoRejectPendingParcelCommand(), CancellationToken.None),
            TimeoutSource.AdditionalPayment => await new ExpireParcelAdditionalPaymentCommandHandler(
                repository,
                identity,
                clock,
                unitOfWork,
                outbox,
                NullLogger<ExpireParcelAdditionalPaymentCommandHandler>.Instance,
                stats).Handle(new ExpireParcelAdditionalPaymentCommand(), CancellationToken.None),
            TimeoutSource.Review => await new ExpireParcelReviewCommandHandler(
                repository,
                clock,
                unitOfWork,
                outbox,
                stats,
                NullLogger<ExpireParcelReviewCommandHandler>.Instance)
                .Handle(new ExpireParcelReviewCommand(), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    private static async Task AssertPersistedStateAsync(
        NpgsqlDataSource dataSource,
        TimeoutSource source,
        Guid parcelId,
        string parcelCode,
        Guid senderUserId,
        Guid operatorId,
        Guid tripId,
        bool failed)
    {
        await using var readContext = CreateDbContext(dataSource);
        var persistedParcel = await readContext.Parcels.AsNoTracking().SingleAsync(row => row.Id == parcelId);
        var outboxRows = await readContext.OutboxEvents.AsNoTracking().ToListAsync();
        var statsRows = await readContext.ParcelStats.AsNoTracking().ToListAsync();

        if (failed)
        {
            persistedParcel.Status.Should().Be(InitialStatus(source));
            persistedParcel.RejectionReason.Should().BeNull();
            outboxRows.Should().BeEmpty();
            statsRows.Should().BeEmpty();
            return;
        }

        var refundAmount = ExpectedRefund(source);
        persistedParcel.Status.Should().Be(
            source == TimeoutSource.Review ? ParcelStatus.CANCELLED : ParcelStatus.REJECTED);
        if (source == TimeoutSource.Review)
        {
            persistedParcel.RejectionReason.Should().BeNull();
            persistedParcel.CancellationReason.Should().Be(ExpectedReason(source));
        }
        else
        {
            persistedParcel.RejectionReason.Should().Be(ExpectedReason(source));
        }
        statsRows.Should().ContainSingle();
        statsRows[0].OperatorId.Should().Be(operatorId);
        statsRows[0].TotalRejected.Should().Be(1);
        statsRows[0].TotalRefunded.Should().Be(refundAmount);

        var expectedRoutingKey = source == TimeoutSource.Review
            ? "parcel.parcel.cancelled"
            : AutoRejectedRoutingKey;
        var autoRejected = outboxRows.Should().ContainSingle(
            row => row.EventType == expectedRoutingKey).Subject;
        autoRejected.Status.Should().Be(OutboxEventStatus.PENDING);
        autoRejected.PublishedAt.Should().BeNull();
        using var json = JsonDocument.Parse(autoRejected.Payload);
        var root = json.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId",
            "occurredAt",
            "parcelId",
            "parcelCode",
            "operatorId",
            "userId",
            "tripId",
            "refundAmount");
        root.GetProperty("eventId").GetGuid().Should().Be(autoRejected.Id);
        root.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
        root.GetProperty("parcelId").GetGuid().Should().Be(parcelId);
        root.GetProperty("parcelCode").GetString().Should().Be(parcelCode);
        root.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        root.GetProperty("userId").GetGuid().Should().Be(senderUserId);
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("refundAmount").GetInt64().Should().Be(refundAmount);

        var refundRows = outboxRows.Where(row => row.EventType == "parcel.refund.initiated").ToList();
        if (source == TimeoutSource.Review)
        {
            refundRows.Should().BeEmpty();
            outboxRows.Should().ContainSingle();
        }
        else
        {
            refundRows.Should().ContainSingle();
            refundRows[0].Status.Should().Be(OutboxEventStatus.PENDING);
            refundRows[0].PublishedAt.Should().BeNull();
            using var refundJson = JsonDocument.Parse(refundRows[0].Payload);
            refundJson.RootElement.GetProperty("parcelId").GetGuid().Should().Be(parcelId);
            refundJson.RootElement.GetProperty("senderUserId").GetGuid().Should().Be(senderUserId);
            refundJson.RootElement.GetProperty("amount").GetInt64().Should().Be(refundAmount);
            outboxRows.Should().HaveCount(2);
        }
    }

    private static ParcelEntity CreateParcel(
        TimeoutSource source,
        Guid senderUserId,
        Guid operatorId,
        Guid tripId)
    {
        var parcelCode = $"VRP-D29-{Guid.NewGuid():N}"[..20];
        var args = new ParcelCreateArguments(parcelCode, senderUserId, operatorId, tripId);
        return source == TimeoutSource.Review
            ? ParcelEntity.CreatePendingOperatorReview(
                args.ParcelCode,
                args.SenderUserId,
                Guid.NewGuid(),
                "Recipient",
                PhoneNumber.Normalize("+84912345678"),
                "recipient@example.com",
                args.OperatorId,
                args.TripId,
                null,
                null,
                "Item",
                null,
                ParcelSizeCategory.MEDIUM,
                5m,
                ParcelDeliveryMethod.TERMINAL_PICKUP,
                Money.FromRaw(100_000))
            : ParcelEntity.CreatePendingPayment(
                args.ParcelCode,
                args.SenderUserId,
                Guid.NewGuid(),
                "Recipient",
                PhoneNumber.Normalize("+84912345678"),
                "recipient@example.com",
                args.OperatorId,
                args.TripId,
                null,
                null,
                "Item",
                null,
                ParcelSizeCategory.MEDIUM,
                5m,
                ParcelDeliveryMethod.TERMINAL_PICKUP,
                Money.FromRaw(100_000));
    }

    private static Task SetTimeoutStateAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        TimeoutSource source)
        => source switch
        {
            TimeoutSource.LateLoad => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = 'PENDING'::vietride_parcel.parcel_status,
                    additional_amount = 25000,
                    updated_at = {Now}
                WHERE id = {parcelId};
                """),
            TimeoutSource.AdditionalPayment => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET status = 'PENDING_ADDITIONAL_PAYMENT'::vietride_parcel.parcel_status,
                    additional_amount = 25000,
                    additional_payment_deadline = {Now.AddMinutes(-1)},
                    updated_at = {Now}
                WHERE id = {parcelId};
                """),
            TimeoutSource.Review => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_parcel.parcels
                SET created_at = {Now.AddHours(-25)},
                    updated_at = {Now.AddHours(-25)}
                WHERE id = {parcelId};
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static ParcelStatus InitialStatus(TimeoutSource source)
        => source switch
        {
            TimeoutSource.LateLoad => ParcelStatus.PENDING,
            TimeoutSource.AdditionalPayment => ParcelStatus.PENDING_ADDITIONAL_PAYMENT,
            TimeoutSource.Review => ParcelStatus.PENDING_OPERATOR_REVIEW,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static long ExpectedRefund(TimeoutSource source)
        => source switch
        {
            TimeoutSource.LateLoad => 125_000,
            TimeoutSource.AdditionalPayment => 100_000,
            TimeoutSource.Review => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static string ExpectedReason(TimeoutSource source)
        => source switch
        {
            TimeoutSource.LateLoad => "PARCEL_LATE_LOAD",
            TimeoutSource.AdditionalPayment => "PARCEL_ADDITIONAL_PAYMENT_TIMEOUT",
            TimeoutSource.Review => "OPERATOR_REVIEW_TIMEOUT",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static TripParcelSnapshot CreateTripSnapshot(Guid operatorId, Guid tripId)
    {
        var departure = Now.AddHours(-1);
        var station = new TripStationDto(Guid.NewGuid(), "Station");
        return new TripParcelSnapshot(
            tripId,
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IN_PROGRESS",
            departure,
            departure.AddHours(4),
            100_000,
            station,
            station,
            [],
            new TripSeatSummaryDto(40, 35),
            null);
    }

    private static TRepository CreateRepository<TRepository>(string typeName, ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        builder.MapEnum<OutboxEventStatus>(
            $"{ParcelDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ParcelDbContext.SchemaName))
            .Options;
        return new ParcelDbContext(options, new FrozenClock(Now));
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        return new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private enum TimeoutSource
    {
        LateLoad,
        AdditionalPayment,
        Review,
    }

    private sealed record ParcelCreateArguments(
        string ParcelCode,
        Guid SenderUserId,
        Guid OperatorId,
        Guid TripId);

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class IdentityStub : IIdentityServiceClient
    {
        private readonly Guid operatorId;

        public IdentityStub(Guid operatorId) => this.operatorId = operatorId;

        public Task<UserLookupOutcome> GetUserInfoAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OperatorLookupOutcome> GetOperatorInfoAsync(
            Guid requestedOperatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(operatorId, "Operator", ParcelNoShowPolicy.Default),
                null));
    }

    private sealed class TripStub : ITripServiceClient
    {
        private readonly TripParcelSnapshot snapshot;

        public TripStub(TripParcelSnapshot snapshot) => this.snapshot = snapshot;

        public Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
            Guid tripId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null));

        public Task<TripCrewAuthorizationOutcome> AuthorizeAssistantForTripAsync(
            Guid tripId, Guid userId, Guid operatorId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
            Guid routeId, Guid operatorId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
            Guid originStationId, Guid destinationStationId, DateOnly departureDate, decimal estimatedWeightKg,
            decimal estimatedVolumeM3, ParcelSizeCategory sizeCategory, int page, int pageSize,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
            Guid originStationId, Guid destinationStationId, DateOnly departureDate, decimal estimatedWeightKg,
            ParcelSizeCategory sizeCategory, int page, int pageSize, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> ReserveCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> ReserveCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
            Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> GetCargoCapacityAsync(
            Guid tripId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> RemeasureCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3,
            bool allowCapacityOverflow = false, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> LoadCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> LoadCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> ReleaseCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripCargoOutcome> ReleaseCargoAsync(
            Guid tripId, Guid parcelId, decimal weightKg, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class CommitFailingUnitOfWork : IUnitOfWork
    {
        private readonly IUnitOfWork inner;

        public CommitFailingUnitOfWork(IUnitOfWork inner) => this.inner = inner;

        public Task<int> SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
            => inner.ExecuteInTransactionAsync(operation, ct);

        public Task BeginTransactionAsync(CancellationToken ct) => inner.BeginTransactionAsync(ct);

        public Task CommitAsync(CancellationToken ct)
            => throw new InvalidOperationException("simulated commit failure");

        public Task RollbackAsync(CancellationToken ct) => inner.RollbackAsync(ct);
    }
}
