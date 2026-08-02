using System.Reflection;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Application.Inbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence.Inbox;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Booking.IntegrationTests;

public sealed class StationMergedInboxAtomicityTests
{
    private const string ConsumerName = "booking.station-merged";
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrueInboxPath_CommitsDomainWriteAndMarker_RollsBothBackOnFailure_AndReplaysNoOp()
    {
        var databaseName = $"vietride_booking_station_inbox_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
        try
        {
            await using (var migrationDb = Day22EventDatabase.CreateDbContext(dataSource, Now))
                await migrationDb.Database.MigrateAsync();

            var successfulEvent = CreateEvent();
            var processed = await ConsumeAsync(dataSource, successfulEvent);
            var replay = await ConsumeAsync(dataSource, successfulEvent);

            processed.Should().Be(IntegrationEventInboxResult.Processed);
            replay.Should().Be(IntegrationEventInboxResult.Duplicate);

            var failedEvent = CreateEvent();
            var failed = () => ConsumeAsync(dataSource, failedEvent, failAfterHandler: true);
            await failed.Should().ThrowAsync<InvalidOperationException>();

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, Now);
            (await verify.BookingStationRedirects.AsNoTracking()
                .CountAsync(row => row.SourceEventId == successfulEvent.EventId))
                .Should().Be(1);
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .CountAsync(row => row.ConsumerName == ConsumerName
                    && row.MessageId == successfulEvent.EventId))
                .Should().Be(1);
            (await verify.BookingStationRedirects.AsNoTracking()
                .AnyAsync(row => row.SourceEventId == failedEvent.EventId))
                .Should().BeFalse();
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .AnyAsync(row => row.ConsumerName == ConsumerName
                    && row.MessageId == failedEvent.EventId))
                .Should().BeFalse();
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task AmbientInbox_WhenLockPlanDrifts_ThrowsAndDoesNotCommitConsumerMarker()
    {
        var databaseName = $"vietride_booking_station_drift_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
        try
        {
            await using (var migrationDb = Day22EventDatabase.CreateDbContext(dataSource, Now))
                await migrationDb.Database.MigrateAsync();

            var integrationEvent = CreateEvent();
            var aliasStationId = Guid.NewGuid();
            await using var blocker = new NpgsqlConnection(connectionString);
            await blocker.OpenAsync();
            await using var blockerTransaction = await blocker.BeginTransactionAsync();
            foreach (var stationId in new[]
                     {
                         integrationEvent.PrimaryStationId,
                         integrationEvent.DuplicateStationId,
                     }.OrderBy(id => id.ToString("D"), StringComparer.Ordinal))
            {
                await using var lockCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended('booking-station:' || @station_id::text, 0))",
                    blocker,
                    blockerTransaction);
                lockCommand.Parameters.AddWithValue("station_id", stationId);
                await lockCommand.ExecuteNonQueryAsync();
            }

            var consume = ConsumeAsync(dataSource, integrationEvent);
            await Day22EventDatabase.WaitForWaitingAdvisoryLockAsync(connectionString);

            await using (var concurrentDb = Day22EventDatabase.CreateDbContext(dataSource, Now))
            {
                concurrentDb.BookingStationRedirects.Add(BookingStationRedirect.Create(
                    aliasStationId,
                    integrationEvent.DuplicateStationId,
                    Guid.NewGuid(),
                    Now.AddSeconds(-1)));
                await concurrentDb.SaveChangesAsync();
            }

            await blockerTransaction.CommitAsync();
            var drift = () => consume.WaitAsync(TimeSpan.FromSeconds(15));
            await drift.Should().ThrowAsync<TransientIntegrationEventException>()
                .WithMessage("*changed while joining the Inbox transaction*");

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, Now);
            (await verify.BookingStationRedirects.AsNoTracking()
                .AnyAsync(row => row.SourceEventId == integrationEvent.EventId))
                .Should().BeFalse();
            (await verify.BookingStationRedirects.AsNoTracking()
                .AnyAsync(row => row.DuplicateStationId == aliasStationId))
                .Should().BeTrue();
            (await verify.Set<IntegrationInboxRecord>().AsNoTracking()
                .AnyAsync(row => row.ConsumerName == ConsumerName
                    && row.MessageId == integrationEvent.EventId))
                .Should().BeFalse();
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public void Registration_ConfiguresDurableDelayedRetryForAmbientLockPlanDrift()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration, registerConsumers: true);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<StationMergedIntegrationEvent>>>()
            .Value.Value;

        options.QueueName.Should().Be(ConsumerName);
        options.BindingKeys.Should().Equal(StationMergedIntegrationEvent.EventType);
        options.TransientRetryCount.Should().Be(3);
        options.TransientRetryDelay.Should().Be(TimeSpan.FromSeconds(10));
    }

    private static async Task<IntegrationEventInboxResult> ConsumeAsync(
        NpgsqlDataSource dataSource,
        StationMergedIntegrationEvent integrationEvent,
        bool failAfterHandler = false)
    {
        await using var db = Day22EventDatabase.CreateDbContext(dataSource, Now);
        var handler = new StationMergedIntegrationEventHandler(CreateRedirectRepository(db));
        var inbox = new EfIntegrationEventInbox<BookingDbContext>(
            db,
            new EfUnitOfWork(db),
            new FixedClock(Now));
        return await inbox.ExecuteAsync(
            ConsumerName,
            integrationEvent.EventId,
            Convert.ToHexString(SHA256.HashData(integrationEvent.EventId.ToByteArray())),
            async cancellationToken =>
            {
                await handler.HandleAsync(integrationEvent, cancellationToken);
                if (failAfterHandler)
                    throw new InvalidOperationException("Injected failure after Station merge handler.");
            },
            CancellationToken.None);
    }

    private static IBookingStationRedirectRepository CreateRedirectRepository(BookingDbContext db)
    {
        var type = typeof(BookingDbContext).Assembly.GetType(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingStationRedirectRepository",
            throwOnError: true)!;
        return (IBookingStationRedirectRepository)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [db, Day22EventDatabase.CreateBookingRepository(db), new FixedClock(Now)],
            culture: null)!;
    }

    private static StationMergedIntegrationEvent CreateEvent()
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAt = Now.UtcDateTime,
            PrimaryStationId = Guid.NewGuid(),
            DuplicateStationId = Guid.NewGuid(),
        };
}
