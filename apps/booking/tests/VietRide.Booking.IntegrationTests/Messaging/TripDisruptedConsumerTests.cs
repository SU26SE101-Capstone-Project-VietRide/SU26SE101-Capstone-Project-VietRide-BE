using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;
using VietRide.Booking.Application.Services;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class TripDisruptedConsumerTests
{
    private static readonly DateTimeOffset TerminalAt =
        DateTimeOffset.Parse("2026-07-30T03:00:00Z");

    [Fact]
    public async Task EligibleStatusesTransitionAtomicallyAndReplayIsNoOp()
    {
        var databaseName = $"vr_d35_disruption_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var originStationId = Guid.NewGuid();
            var confirmed = CreateBooking(
                tripId,
                operatorId,
                originStationId,
                400_000,
                BookingStatus.CONFIRMED);
            var partial = CreateBooking(
                tripId,
                operatorId,
                originStationId,
                500_000,
                BookingStatus.PARTIAL_NO_SHOW);
            var noShow = CreateBooking(
                tripId,
                operatorId,
                originStationId,
                600_000,
                BookingStatus.NO_SHOW);

            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.AddRange(confirmed, partial, noShow);
                await seed.SaveChangesAsync();
            }

            var command = new HandleTripDisruptedCommand(
                Guid.NewGuid(),
                TerminalAt,
                tripId,
                operatorId,
                TerminalAt,
                HasSubstitution: false,
                "Road closure");
            await using (var consume = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt))
            {
                var affected = await CreateHandler(
                        consume,
                        Trip(tripId, operatorId, originStationId))
                    .Handle(command, CancellationToken.None);
                affected.Should().Be(2);
            }

            await using (var replay = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt))
            {
                var affected = await CreateHandler(
                        replay,
                        Trip(tripId, operatorId, originStationId))
                    .Handle(command, CancellationToken.None);
                affected.Should().Be(0);
            }

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt);
            var rows = await verify.Bookings.AsNoTracking().ToArrayAsync();
            rows.Single(row => row.Id == confirmed.Id).Status.Should().Be(BookingStatus.DISRUPTED);
            rows.Single(row => row.Id == partial.Id).Status.Should().Be(BookingStatus.DISRUPTED);
            rows.Single(row => row.Id == noShow.Id).Status.Should().Be(BookingStatus.NO_SHOW);
            rows.Where(row => row.Status == BookingStatus.DISRUPTED).Should().OnlyContain(row =>
                row.CancellationReason == BookingCancellationReason.OPERATOR_DISRUPTED_IN_PROGRESS
                && row.RefundOverride
                && row.CancelledAt == TerminalAt);

            var outbox = await verify.OutboxEvents.AsNoTracking().ToArrayAsync();
            outbox.Count(row => row.EventType == BookingCancelledIntegrationEvent.EventTypeValue)
                .Should().Be(2);
            outbox.Count(row => row.EventType == BookingDisruptedIntegrationEvent.EventTypeValue)
                .Should().Be(2);
            outbox.Should().OnlyContain(row =>
                ReadEventId(row.Payload) == row.Id
                && row.PublishedAt == null);
            (await verify.BookingStatusHistories.AsNoTracking().CountAsync(row =>
                row.Status == BookingStatus.DISRUPTED)).Should().Be(2);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentDeliveriesSerializeOnPostgresLocksAndApplySideEffectsOnce(
        bool sameEventId)
    {
        var databaseName = $"vr_d35_disruption_concurrency_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);
        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var tripId = Guid.NewGuid();
            var operatorId = Guid.NewGuid();
            var originStationId = Guid.NewGuid();
            var confirmed = CreateBooking(
                tripId,
                operatorId,
                originStationId,
                400_000,
                BookingStatus.CONFIRMED,
                discountAmount: 25_000);
            var partial = CreateBooking(
                tripId,
                operatorId,
                originStationId,
                500_000,
                BookingStatus.PARTIAL_NO_SHOW,
                discountAmount: 30_000);
            var voucher = Voucher.Create(
                $"D35-{Guid.NewGuid():N}",
                "Day 35 disruption concurrency",
                VoucherType.FIXED_AMOUNT,
                50_000,
                Money.Zero,
                maxDiscountAmount: null,
                totalUsageLimit: null,
                perUserLimit: null,
                validFrom: TerminalAt.AddDays(-1),
                validUntil: TerminalAt.AddDays(1),
                applicableOperatorIds: null,
                applicableRouteIds: null,
                fundingType: VoucherFundingType.VIETRIDE_FUNDED,
                ownerOperatorId: null,
                createdByUserId: Guid.NewGuid());
            var confirmedUsage = VoucherUsage.Create(
                voucher.Id,
                confirmed.PassengerUserId,
                "BOOKING",
                confirmed.Id,
                bookingGroupId: null,
                Money.FromRaw(25_000),
                voucher.FundingType);
            var partialUsage = VoucherUsage.Create(
                voucher.Id,
                partial.PassengerUserId,
                "BOOKING",
                partial.Id,
                bookingGroupId: null,
                Money.FromRaw(30_000),
                voucher.FundingType);

            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt))
            {
                await seed.Database.MigrateAsync();
                seed.Vouchers.Add(voucher);
                seed.Bookings.AddRange(confirmed, partial);
                seed.VoucherUsages.AddRange(confirmedUsage, partialUsage);
                await seed.SaveChangesAsync();
                (await seed.VoucherUsages.CountAsync()).Should().Be(2);
            }

            var firstEventId = Guid.NewGuid();
            var secondEventId = sameEventId ? firstEventId : Guid.NewGuid();
            var firstCommand = CreateCommand(firstEventId, tripId, operatorId);
            var secondCommand = CreateCommand(secondEventId, tripId, operatorId);
            var trip = Trip(tripId, operatorId, originStationId);
            var compensationCalls = new ConcurrentDictionary<Guid, int>();
            var firstHasLockedRows = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await using var firstDb = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt);
            await using var secondDb = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt);
            var firstHandler = CreateHandler(
                firstDb,
                trip,
                CreateTrackedVoucherService(firstDb, compensationCalls),
                async cancellationToken =>
                {
                    firstHasLockedRows.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                });
            var secondHandler = CreateHandler(
                secondDb,
                trip,
                CreateTrackedVoucherService(secondDb, compensationCalls));

            var firstDelivery = firstHandler.Handle(firstCommand, CancellationToken.None);
            await firstHasLockedRows.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var secondDelivery = secondHandler.Handle(secondCommand, CancellationToken.None);
            try
            {
                if (sameEventId)
                {
                    await Day22EventDatabase.WaitForWaitingAdvisoryLockAsync(connectionString);
                }
                else
                {
                    await WaitForWaitingDatabaseLockAsync(connectionString);
                }
            }
            finally
            {
                releaseFirst.TrySetResult();
            }

            var results = await Task.WhenAll(firstDelivery, secondDelivery)
                .WaitAsync(TimeSpan.FromSeconds(15));
            results.Should().Equal(2, 0);

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, TerminalAt);
            var bookingIds = new[] { confirmed.Id, partial.Id };
            var bookings = await verify.Bookings.AsNoTracking()
                .Where(row => bookingIds.Contains(row.Id))
                .ToArrayAsync();
            bookings.Should().HaveCount(2);
            bookings.Should().OnlyContain(row =>
                row.Status == BookingStatus.DISRUPTED
                && row.CancellationReason
                    == BookingCancellationReason.OPERATOR_DISRUPTED_IN_PROGRESS);

            var histories = await verify.BookingStatusHistories.AsNoTracking()
                .Where(row =>
                    bookingIds.Contains(row.BookingId)
                    && row.Status == BookingStatus.DISRUPTED)
                .ToArrayAsync();
            histories.Should().HaveCount(2);
            foreach (var bookingId in bookingIds)
            {
                histories.Count(row =>
                        row.BookingId == bookingId
                        && row.Source == BookingStatusHistorySource.DisruptOnTripDisrupted)
                    .Should().Be(1);
            }

            var outbox = await verify.OutboxEvents.AsNoTracking().ToArrayAsync();
            outbox.Should().HaveCount(4);
            foreach (var bookingId in bookingIds)
            {
                outbox.Count(row =>
                        row.EventType == BookingCancelledIntegrationEvent.EventTypeValue
                        && ReadBookingId(row.Payload) == bookingId)
                    .Should().Be(1);
                outbox.Count(row =>
                        row.EventType == BookingDisruptedIntegrationEvent.EventTypeValue
                        && ReadBookingId(row.Payload) == bookingId)
                    .Should().Be(1);
            }

            outbox.Should().OnlyContain(row =>
                bookingIds.Contains(ReadBookingId(row.Payload))
                && ReadEventId(row.Payload) == row.Id
                && row.PublishedAt == null);
            (await verify.VoucherUsages.AsNoTracking().CountAsync()).Should().Be(0);
            compensationCalls.Should().HaveCount(2);
            compensationCalls[confirmed.Id].Should().Be(1);
            compensationCalls[partial.Id].Should().Be(1);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static HandleTripDisruptedCommandHandler CreateHandler(
        BookingDbContext db,
        TripSnapshot trip,
        IVoucherService? voucherService = null,
        Func<CancellationToken, Task>? onTripSnapshot = null)
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        tripClient.GetOperationalTripSnapshotAsync(
                trip.TripId,
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (onTripSnapshot is not null)
                {
                    await onTripSnapshot(call.ArgAt<CancellationToken>(1));
                }

                return trip;
            });
        return new HandleTripDisruptedCommandHandler(
            Day22EventDatabase.CreateBookingRepository(db),
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            tripClient,
            voucherService ?? Substitute.For<IVoucherService>(),
            new IntegrationEventOutbox(new OutboxStore(db, new FixedClock(TerminalAt))),
            new EfUnitOfWork(db),
            new FixedClock(TerminalAt.AddSeconds(1)));
    }

    private static HandleTripDisruptedCommand CreateCommand(
        Guid eventId,
        Guid tripId,
        Guid operatorId)
        => new(
            eventId,
            TerminalAt,
            tripId,
            operatorId,
            TerminalAt,
            HasSubstitution: false,
            "Road closure");

    private static IVoucherService CreateTrackedVoucherService(
        BookingDbContext db,
        ConcurrentDictionary<Guid, int> compensationCalls)
    {
        var voucherService = new VoucherService(
            CreateRepository<IVoucherRepository>(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.VoucherRepository",
                db),
            Substitute.For<IOperatorVoucherConsentRepository>(),
            Day22EventDatabase.CreateBookingRepository(db),
            NullLogger<VoucherService>.Instance);
        var tracked = Substitute.For<IVoucherService>();
        tracked.CompensateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var bookingId = call.ArgAt<Guid>(0);
                compensationCalls.AddOrUpdate(
                    bookingId,
                    1,
                    static (_, count) => count + 1);
                return voucherService.CompensateAsync(
                    bookingId,
                    call.ArgAt<CancellationToken>(1));
            });
        return tracked;
    }

    private static async Task WaitForWaitingDatabaseLockAsync(string connectionString)
    {
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid()
                      AND state = 'active'
                      AND wait_event_type = 'Lock')
                """, observer);
            command.Parameters.AddWithValue("database_name", databaseName!);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            "The concurrent delivery never reached the PostgreSQL row-lock wait state.");
    }

    private static T CreateRepository<T>(string typeName, BookingDbContext db)
        => (T)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(typeName, throwOnError: true)!,
            db)!;

    private static TripSnapshot Trip(
        Guid tripId,
        Guid operatorId,
        Guid originStationId)
        => new(
            tripId,
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DISRUPTED",
            TerminalAt.AddHours(-5),
            TerminalAt.AddHours(5),
            100_000,
            new TripStationSnapshot(originStationId, "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            [],
            new TripSeatSummary(40, 10),
            TotalDistanceKm: null);

    private static BookingEntity CreateBooking(
        Guid tripId,
        Guid operatorId,
        Guid originStationId,
        long totalAmount,
        BookingStatus status,
        long discountAmount = 0)
    {
        var subtotalAmount = checked(totalAmount + discountAmount);
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(TerminalAt),
            Guid.NewGuid(),
            tripId,
            operatorId,
            originStationId,
            null,
            null,
            null,
            Money.FromRaw(subtotalAmount),
            Money.FromRaw(discountAmount),
            Money.FromRaw(totalAmount));
        booking.Confirm(TerminalAt.AddHours(-6));
        SetStatus(booking, status);
        return booking;
    }

    private static Guid ReadEventId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("eventId").GetGuid();
    }

    private static Guid ReadBookingId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("bookingId").GetGuid();
    }

    private static void SetStatus(BookingEntity booking, BookingStatus status)
        => typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(booking, status);
}
