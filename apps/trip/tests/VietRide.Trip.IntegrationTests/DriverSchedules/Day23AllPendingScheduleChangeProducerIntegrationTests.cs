using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Persistence.Repositories;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.DriverSchedules;

public sealed class Day23AllPendingScheduleChangeProducerIntegrationTests
{
    private const string ScratchDatabasePrefix = "vietride_day23_schedule_producer_";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TripAndScheduleChangedOutbox_CommitOrRollbackInOneTransaction(bool commit)
    {
        var databaseName = $"{ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);

        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db);
            db.ChangeTracker.Clear();

            var clock = new FixedClock(Now);
            var durableOutbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
            IIntegrationEventOutbox outbox = commit
                ? durableOutbox
                : new ThrowAfterStagingOutbox(durableOutbox);
            var unitOfWork = new RecordingUnitOfWork(new EfUnitOfWork(db));
            var handler = CreateHandler(db, seed, outbox, unitOfWork, clock);
            var command = CreateCommand(seed, new TimeOnly(21, 0));

            if (commit)
            {
                await handler.Handle(command, CancellationToken.None);
            }
            else
            {
                var action = () => handler.Handle(command, CancellationToken.None);
                await action.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("staged outbox failure");
            }

            await using var assertionDb = CreateDbContext(databaseName);
            var schedule = await assertionDb.DriverSchedules.AsNoTracking()
                .SingleAsync(item => item.Id == seed.ScheduleId);
            var trip = await assertionDb.Trips.AsNoTracking()
                .SingleAsync(item => item.Id == seed.TripId);
            var outboxRows = await assertionDb.OutboxEvents.AsNoTracking()
                .Where(item => item.EventType == TripScheduleChangedIntegrationEvent.EventTypeValue)
                .ToArrayAsync();
            var scheduleAudits = await assertionDb.DriverScheduleAuditLogs.AsNoTracking()
                .Where(item => item.DriverScheduleId == seed.ScheduleId)
                .ToArrayAsync();
            var tripAudits = await assertionDb.TripAuditLogs.AsNoTracking()
                .Where(item => item.TripId == seed.TripId)
                .ToArrayAsync();

            if (commit)
            {
                unitOfWork.Calls.Should().Equal("begin", "commit");
                schedule.DepartureTime.Should().Be(new TimeOnly(21, 0));
                trip.DepartureDateTime.Should().Be(seed.NewDeparture);
                scheduleAudits.Should().ContainSingle();
                tripAudits.Should().ContainSingle();
                outboxRows.Should().ContainSingle();
                outboxRows[0].Status.Should().Be(OutboxEventStatus.PENDING);
                using var payload = JsonDocument.Parse(outboxRows[0].Payload);
                payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outboxRows[0].Id);
                payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(seed.TripId);
                payload.RootElement.GetProperty("oldDeparture").GetDateTimeOffset().Should().Be(seed.OldDeparture);
                payload.RootElement.GetProperty("newDeparture").GetDateTimeOffset().Should().Be(seed.NewDeparture);
                payload.RootElement.GetProperty("severity").GetString().Should().Be("MINOR");
            }
            else
            {
                unitOfWork.Calls.Should().Equal("begin", "rollback");
                schedule.DepartureTime.Should().Be(new TimeOnly(20, 0));
                trip.DepartureDateTime.Should().Be(seed.OldDeparture);
                scheduleAudits.Should().BeEmpty();
                tripAudits.Should().BeEmpty();
                outboxRows.Should().BeEmpty();
            }
        }
        finally
        {
            await DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static UpdateDriverScheduleHandler CreateHandler(
        TripDbContext db,
        Seed seed,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock) =>
        new(
            new DriverScheduleRepository(db),
            new DriverScheduleAuditLogRepository(db),
            CreateInternal<ITripRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
                db),
            CreateInternal<ITripSeatRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.TripSeatRepository",
                db),
            CreateInternal<ITripStopRepository>(
                "VietRide.Trip.Infrastructure.Persistence.Repositories.TripStopRepository",
                db),
            new TripAuditLogRepository(db),
            Unexpected<IVehicleRepository>(),
            Unexpected<IRouteRepository>(),
            new AllowedIdentityClient(),
            new BookingClient(seed.TripId),
            Unexpected<ITripVehicleSwapService>(),
            outbox,
            new JobScheduler(),
            unitOfWork,
            clock);

    private static UpdateDriverScheduleCommand CreateCommand(Seed seed, TimeOnly departureTime) =>
        new(
            seed.OperatorId,
            seed.ScheduleId,
            Guid.NewGuid(),
            "day23-integration",
            UpdateDriverScheduleCommand.AllPending,
            DepartureTimeSpecified: true,
            DepartureTime: departureTime,
            DayOfWeekSpecified: false,
            DayOfWeek: null,
            DriverUserIdSpecified: false,
            DriverUserId: null,
            AssistantUserIdSpecified: false,
            AssistantUserId: null,
            VehicleIdSpecified: false,
            VehicleId: null,
            ValidUntilSpecified: false,
            ValidUntil: null,
            IsActiveSpecified: false,
            IsActive: null);

    private static async Task<Seed> SeedAsync(TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Day 23 producer origin",
            $"day23-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City");
        var destination = Station.Create(
            "Day 23 producer destination",
            $"day23-destination-{Guid.NewGuid():N}",
            "Da Nang",
            "Da Nang");
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Day 23 producer route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            totalDistanceKm: 100m,
            estimatedDurationMinutes: 240);
        var vehicleType = VehicleType.Create(
            $"DAY23_{Guid.NewGuid():N}",
            "Day 23 producer vehicle",
            estimatedPassengerLuggageKgPerSeat: null,
            defaultSeatCount: 1);
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"D23-{Guid.NewGuid():N}"[..20],
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "DAY23",
                totalSeats = 1,
                rows = 1,
                cols = 1,
                decks = 1,
                aisles = Array.Empty<object>(),
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        row = 1,
                        col = 1,
                        deck = 1,
                        type = "STANDARD",
                        isWindow = true,
                        isAisle = false,
                        disabled = false,
                    },
                },
            }),
            totalSeats: 1,
            maxCargoWeightKg: null,
            maxCargoVolumeM3: null);
        var serviceDate = new DateOnly(2026, 7, 16);
        var oldDeparture = BuildDeparture(serviceDate, new TimeOnly(20, 0));
        var newDeparture = BuildDeparture(serviceDate, new TimeOnly(21, 0));
        var driverUserId = Guid.NewGuid();
        var schedule = DriverSchedule.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            driverUserId,
            assistantUserId: null,
            JsonSerializer.SerializeToElement(new[] { 4 }),
            new TimeOnly(20, 0),
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 12, 31),
            isActive: true);
        var trip = TripEntity.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            driverUserId,
            assistantUserId: null,
            schedule.Id,
            oldDeparture,
            oldDeparture.AddHours(4),
            TripSource.AUTO_FROM_SCHEDULE,
            Money.FromRaw(100_000),
            maxCargoWeightKg: null,
            estimatedPassengerLuggageKg: 0m);

        db.AddRange(origin, destination, route, vehicleType, vehicle, schedule, trip);
        await db.SaveChangesAsync();
        return new Seed(operatorId, schedule.Id, trip.Id, oldDeparture, newDeparture);
    }

    private static DateTimeOffset BuildDeparture(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(7)).ToUniversalTime();

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        builder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(builder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(builder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new FixedClock(Now));
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        var connectionString = new NpgsqlConnectionStringBuilder(expanded)
        {
            Database = databaseName,
        };

        return connectionString.ConnectionString;
    }

    private static async Task DeleteScratchDatabaseAsync(TripDbContext db, string databaseName)
    {
        var connectedDatabase = db.Database.GetDbConnection().Database;
        if (!databaseName.StartsWith(ScratchDatabasePrefix, StringComparison.Ordinal)
            || !connectedDatabase.StartsWith(ScratchDatabasePrefix, StringComparison.Ordinal)
            || !string.Equals(connectedDatabase, databaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to delete non-scratch database '{connectedDatabase}'.");
        }

        await db.Database.EnsureDeletedAsync();
    }

    private static T CreateInternal<T>(string typeName, TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (T)Activator.CreateInstance(type, db)!;
    }

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No Identity lookup is expected for a departure-only update.");
    }

    private sealed class BookingClient(Guid tripId) : IBookingImpactClient
    {
        public Task<int> GetActiveBookingCountByStopAsync(
            Guid stopId,
            Guid operatorId,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid requestedTripId,
            Guid operatorId,
            CancellationToken cancellationToken)
        {
            requestedTripId.Should().Be(tripId);
            return Task.FromResult(new TripBookingImpactProjection(
                tripId,
                1,
                [new TripBookingImpactProjection.ActiveBooking(Guid.NewGuid(), "CONFIRMED", [])]));
        }
    }

    private sealed class ThrowAfterStagingOutbox(IIntegrationEventOutbox inner) : IIntegrationEventOutbox
    {
        public async Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            await inner.EnqueueAsync(eventType, payloadJson, cancellationToken);
            throw new InvalidOperationException("staged outbox failure");
        }

        public async Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            await inner.EnqueueAsync(eventId, eventType, payloadJson, cancellationToken);
            throw new InvalidOperationException("staged outbox failure");
        }
    }

    private sealed class RecordingUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            inner.SaveChangesAsync(cancellationToken);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(operation, cancellationToken);

        public async Task BeginTransactionAsync(CancellationToken cancellationToken)
        {
            Calls.Add("begin");
            await inner.BeginTransactionAsync(cancellationToken);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            Calls.Add("commit");
            await inner.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            Calls.Add("rollback");
            await inner.RollbackAsync(cancellationToken);
        }
    }

    private sealed class JobScheduler : ITripGenerationJobScheduler
    {
        public string EnqueueScheduleGeneration(Guid driverScheduleId) => driverScheduleId.ToString("N");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static T Unexpected<T>()
        where T : class => DispatchProxy.Create<T, UnexpectedDependencyProxy>();

    public class UnexpectedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"Unexpected dependency call: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }

    private sealed record Seed(
        Guid OperatorId,
        Guid ScheduleId,
        Guid TripId,
        DateTimeOffset OldDeparture,
        DateTimeOffset NewDeparture);
}
