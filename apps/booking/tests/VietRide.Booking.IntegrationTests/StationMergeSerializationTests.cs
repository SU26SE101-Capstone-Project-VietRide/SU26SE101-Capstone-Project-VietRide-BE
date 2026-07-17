using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
using VietRide.Booking.Application.Features.Bookings.EditDropoff;
using VietRide.Booking.Application.Features.Bookings.EditPickup;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class StationMergeSerializationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyMerge_ReplayIsNoOp_AndConflictingDuplicateRollsBack()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_replay");
        await using var db = database.CreateDbContext();
        var redirects = CreateRedirectRepository(db, new FixedClock(Now));
        var eventId = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        var canonical = Guid.NewGuid();

        var first = await redirects.ApplyMergeAsync(eventId, Now, canonical, duplicate);
        var replay = await redirects.ApplyMergeAsync(eventId, Now, canonical, duplicate);
        var mismatchedReplay = () => redirects.ApplyMergeAsync(
            eventId,
            Now,
            canonical,
            Guid.NewGuid());
        var conflictingEventId = Guid.NewGuid();
        var conflict = () => redirects.ApplyMergeAsync(
            conflictingEventId,
            Now.AddSeconds(1),
            Guid.NewGuid(),
            duplicate);

        await mismatchedReplay.Should().ThrowAsync<InvalidOperationException>();
        await conflict.Should().ThrowAsync<InvalidOperationException>();
        first.Applied.Should().BeTrue();
        replay.Applied.Should().BeFalse();
        replay.CanonicalStationId.Should().Be(canonical);
        (await db.BookingStationRedirects.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.BookingStationRedirects.AsNoTracking()
            .AnyAsync(row => row.SourceEventId == conflictingEventId)).Should().BeFalse();
    }

    [Fact]
    public async Task Migration_DownAndReapply_IsReversibleAndRestoresRedirectTable()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_migration");
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        (await RedirectTableExistsAsync(db)).Should().BeTrue();
        await migrator.MigrateAsync("20260712182713_AddBookingShuttleIntent");
        (await RedirectTableExistsAsync(db)).Should().BeFalse();
        await migrator.MigrateAsync();
        (await RedirectTableExistsAsync(db)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyMerge_AtoBtoC_InEitherEventOrder_FlattensAliasesAndRetainsSourceEvents(
        bool reverseOrder)
    {
        await using var database = await TestDatabase.CreateAsync("redirect_order");
        await using var db = database.CreateDbContext();
        var redirects = CreateRedirectRepository(db, new FixedClock(Now));
        var stationA = Guid.NewGuid();
        var stationB = Guid.NewGuid();
        var stationC = Guid.NewGuid();
        var eventAtoB = Guid.NewGuid();
        var eventBtoC = Guid.NewGuid();

        if (reverseOrder)
        {
            await redirects.ApplyMergeAsync(eventBtoC, Now, stationC, stationB);
            await redirects.ApplyMergeAsync(eventAtoB, Now.AddSeconds(1), stationB, stationA);
        }
        else
        {
            await redirects.ApplyMergeAsync(eventAtoB, Now, stationB, stationA);
            await redirects.ApplyMergeAsync(eventBtoC, Now.AddSeconds(1), stationC, stationB);
        }

        var rows = await db.BookingStationRedirects.AsNoTracking()
            .OrderBy(row => row.DuplicateStationId)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(row =>
            row.DuplicateStationId == stationA
            && row.CanonicalStationId == stationC
            && row.SourceEventId == eventAtoB);
        rows.Should().ContainSingle(row =>
            row.DuplicateStationId == stationB
            && row.CanonicalStationId == stationC
            && row.SourceEventId == eventBtoC);
    }

    [Fact]
    public async Task ApplyMerge_ConcurrentChainEvents_EndWithFlatGraph()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_concurrent");
        var stationA = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var stationB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var stationC = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var eventAtoB = Guid.NewGuid();
        var eventBtoC = Guid.NewGuid();

        await using var firstDb = database.CreateDbContext();
        await using var secondDb = database.CreateDbContext();
        var first = CreateRedirectRepository(firstDb, new FixedClock(Now));
        var second = CreateRedirectRepository(secondDb, new FixedClock(Now));

        await Task.WhenAll(
            first.ApplyMergeAsync(eventAtoB, Now, stationB, stationA),
            second.ApplyMergeAsync(eventBtoC, Now.AddSeconds(1), stationC, stationB))
            .WaitAsync(TimeSpan.FromSeconds(20));

        await using var readDb = database.CreateDbContext();
        var rows = await readDb.BookingStationRedirects.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.CanonicalStationId == stationC);
        rows.Select(row => row.SourceEventId).Should().BeEquivalentTo([eventAtoB, eventBtoC]);
    }

    [Fact]
    public async Task ApplyMerge_CycleSelfAndHopGuard_DoNotCreateMarkerOrRelink()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_guards");
        await using var db = database.CreateDbContext();
        var redirects = CreateRedirectRepository(db, new FixedClock(Now));
        var stationA = Guid.NewGuid();
        var stationB = Guid.NewGuid();
        var firstEventId = Guid.NewGuid();
        await redirects.ApplyMergeAsync(firstEventId, Now, stationB, stationA);

        var cycleEventId = Guid.NewGuid();
        var cycle = () => redirects.ApplyMergeAsync(
            cycleEventId,
            Now.AddSeconds(1),
            stationA,
            stationB);
        var selfEventId = Guid.NewGuid();
        var self = () => redirects.ApplyMergeAsync(
            selfEventId,
            Now.AddSeconds(2),
            stationA,
            stationA);

        await cycle.Should().ThrowAsync<InvalidOperationException>();
        await self.Should().ThrowAsync<InvalidOperationException>();

        var longChain = Enumerable.Range(0, 34).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < 33; index++)
        {
            db.BookingStationRedirects.Add(BookingStationRedirect.Create(
                longChain[index],
                longChain[index + 1],
                Guid.NewGuid(),
                Now));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var hopEventId = Guid.NewGuid();
        var tooDeep = () => redirects.ApplyMergeAsync(
            hopEventId,
            Now.AddSeconds(3),
            longChain[0],
            Guid.NewGuid());
        await tooDeep.Should().ThrowAsync<InvalidOperationException>();

        var sourceEventIds = await db.BookingStationRedirects.AsNoTracking()
            .Select(row => row.SourceEventId)
            .ToListAsync();
        sourceEventIds.Should().Contain(firstEventId);
        sourceEventIds.Should().NotContain([cycleEventId, selfEventId, hopEventId]);
    }

    [Fact]
    public async Task ApplyMerge_RelinksOnlyActiveBookings()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_relink");
        await using var db = database.CreateDbContext();
        var duplicate = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        var pending = CreateBooking(duplicate, Guid.NewGuid(), Guid.NewGuid());
        var confirmed = CreateBooking(duplicate, Guid.NewGuid(), Guid.NewGuid());
        var completed = CreateBooking(duplicate, Guid.NewGuid(), Guid.NewGuid());
        db.Bookings.AddRange(pending, confirmed, completed);
        await db.SaveChangesAsync();
        await SetStatusAsync(db, confirmed.Id, BookingStatus.CONFIRMED);
        await SetStatusAsync(db, completed.Id, BookingStatus.COMPLETED);
        db.ChangeTracker.Clear();

        var result = await CreateRedirectRepository(db, new FixedClock(Now)).ApplyMergeAsync(
            Guid.NewGuid(),
            Now,
            canonical,
            duplicate);

        var rows = await db.Bookings.AsNoTracking()
            .Where(row => row.Id == pending.Id || row.Id == confirmed.Id || row.Id == completed.Id)
            .ToDictionaryAsync(row => row.Id);
        result.RelinkedBookingCount.Should().Be(2);
        rows[pending.Id].PickupStationId.Should().Be(canonical);
        rows[confirmed.Id].PickupStationId.Should().Be(canonical);
        rows[completed.Id].PickupStationId.Should().Be(duplicate);
    }

    [Fact]
    public async Task ApplyMerge_WhenMarkerInsertFails_RollsBackBookingRelink()
    {
        await using var database = await TestDatabase.CreateAsync("redirect_atomicity");
        await using var db = database.CreateDbContext();
        var duplicate = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        var booking = CreateBooking(duplicate, Guid.NewGuid(), Guid.NewGuid());
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION vietride_booking.reject_station_redirect_insert()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'forced marker failure';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER reject_station_redirect_insert
            BEFORE INSERT ON vietride_booking.booking_station_redirects
            FOR EACH ROW EXECUTE FUNCTION vietride_booking.reject_station_redirect_insert();
            """);

        var apply = () => CreateRedirectRepository(db, new FixedClock(Now)).ApplyMergeAsync(
            Guid.NewGuid(),
            Now,
            canonical,
            duplicate);

        await apply.Should().ThrowAsync<Exception>();
        db.ChangeTracker.Clear();
        var persisted = await db.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
        persisted.PickupStationId.Should().Be(duplicate);
        (await db.BookingStationRedirects.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(StationWriter.CreateBooking)]
    [InlineData(StationWriter.CreateRoundTripBooking)]
    [InlineData(StationWriter.EditPickup)]
    [InlineData(StationWriter.EditDropoff)]
    public async Task ConsumerVsStationWriter_FiftyIterations_PreservesSerializationInvariants(
        StationWriter writerKind)
    {
        await using var database = await TestDatabase.CreateAsync($"station_race_{writerKind}");
        var clock = new FixedClock(Now);

        for (var iteration = 0; iteration < 50; iteration++)
        {
            var duplicate = Guid.NewGuid();
            var canonical = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var passengerUserId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var terminalBookingId = await SeedTerminalBookingAsync(
                database,
                duplicate,
                Guid.NewGuid(),
                Guid.NewGuid());
            var editableBookingId = writerKind is StationWriter.EditPickup or StationWriter.EditDropoff
                ? await SeedConfirmedBookingAsync(database, duplicate, passengerUserId, tripId, writerKind)
                : (Guid?)null;

            await using var writerDb = database.CreateDbContext();
            await using var consumerDb = database.CreateDbContext();
            var realCanonicalizer = CreateCanonicalizer(writerDb);
            var barrier = new BeforeAdvisoryLockBarrier(realCanonicalizer);
            var writerTask = RunWriterAsync(
                writerKind,
                writerDb,
                barrier,
                clock,
                duplicate,
                passengerUserId,
                tripId,
                editableBookingId,
                iteration);

            await barrier.Arrived.WaitAsync(TimeSpan.FromSeconds(10));
            var redirects = CreateRedirectRepository(consumerDb, clock);
            Task<BookingStationMergeApplicationResult> consumerTask;
            try
            {
                if (iteration % 3 == 0)
                {
                    consumerTask = redirects.ApplyMergeAsync(eventId, Now, canonical, duplicate);
                    await consumerTask.WaitAsync(TimeSpan.FromSeconds(10));
                    barrier.Release();
                }
                else if (iteration % 3 == 1)
                {
                    consumerTask = redirects.ApplyMergeAsync(eventId, Now, canonical, duplicate);
                    barrier.Release();
                }
                else
                {
                    barrier.Release();
                    await Task.Yield();
                    consumerTask = redirects.ApplyMergeAsync(eventId, Now, canonical, duplicate);
                }

                await Task.WhenAll(writerTask, consumerTask).WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                barrier.Release();
            }

            await using var readDb = database.CreateDbContext();
            var activeRows = await readDb.Bookings.AsNoTracking()
                .Where(row => row.PassengerUserId == passengerUserId
                    && (row.Status == BookingStatus.PENDING_PAYMENT || row.Status == BookingStatus.CONFIRMED))
                .ToListAsync();
            activeRows.Should().NotBeEmpty(because: $"writer {writerKind}, iteration {iteration}");
            activeRows.Should().OnlyContain(
                row => row.PickupStationId != duplicate && row.DropoffStationId != duplicate,
                because: $"writer {writerKind}, iteration {iteration}");

            var terminal = await readDb.Bookings.AsNoTracking()
                .SingleAsync(row => row.Id == terminalBookingId);
            terminal.Status.Should().Be(BookingStatus.COMPLETED);
            terminal.PickupStationId.Should().Be(duplicate);

            var markers = await readDb.BookingStationRedirects.AsNoTracking()
                .Where(row => row.SourceEventId == eventId)
                .ToListAsync();
            markers.Should().ContainSingle();
            markers[0].DuplicateStationId.Should().Be(duplicate);
            markers[0].CanonicalStationId.Should().Be(canonical);
        }
    }

    private static async Task RunWriterAsync(
        StationWriter writerKind,
        BookingDbContext db,
        IBookingStationCanonicalizer canonicalizer,
        IClock clock,
        Guid duplicateStationId,
        Guid passengerUserId,
        Guid tripId,
        Guid? editableBookingId,
        int iteration)
    {
        var bookings = CreateBookingRepository(db);
        var statusHistory = Substitute.For<IBookingStatusHistoryRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var paymentClient = Substitute.For<IPaymentServiceClient>();
        var bookingService = Substitute.For<IBookingService>();
        var voucherService = Substitute.For<IVoucherService>();
        var voucherRepository = Substitute.For<IVoucherRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var operatorId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var trip = CreateTripSnapshot(
            tripId,
            operatorId,
            duplicateStationId,
            destinationStationId,
            returnRouteId: Guid.NewGuid());
        tripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
        tripClient.GetTripSnapshotAsync(
                tripId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(trip);
        tripClient.LockSeatsAsync(default, default!, default, default!, default, default)
            .ReturnsForAnyArgs(new LockSeatsOutcome.Success(
                new SeatLockResult(Guid.NewGuid(), ["A01"], Now.AddMinutes(10))));
        tripClient.BookSeatsAsync(default, default, default, default!, default)
            .ReturnsForAnyArgs(true);
        paymentClient.ChargeAsync(default!, default, default, default, default!, default!, default, default)
            .ReturnsForAnyArgs(new ChargeOutcome.Success(
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null)));

        var unitOfWork = new EfUnitOfWork(db);
        switch (writerKind)
        {
            case StationWriter.CreateBooking:
                {
                    var handler = new CreateBookingCommandHandler(
                        bookings,
                        tripClient,
                        paymentClient,
                        bookingService,
                        voucherService,
                        outbox,
                        clock,
                        NullLogger<CreateBookingCommandHandler>.Instance,
                        statusHistory,
                        canonicalizer);
                    var command = new CreateBookingCommand(
                        passengerUserId,
                        tripId,
                        duplicateStationId,
                        null,
                        null,
                        null,
                        [new SeatRequest($"A{iteration:D2}")],
                        null,
                        "WALLET");
                    _ = await unitOfWork.ExecuteInTransactionAsync(
                        () => handler.Handle(command, CancellationToken.None),
                        CancellationToken.None);
                    break;
                }

            case StationWriter.CreateRoundTripBooking:
                {
                    var returnTripId = Guid.NewGuid();
                    var returnTrip = CreateTripSnapshot(
                        returnTripId,
                        operatorId,
                        duplicateStationId,
                        Guid.NewGuid(),
                        departureOffsetHours: 10);
                    tripClient.GetTripSnapshotAsync(returnTripId, Arg.Any<CancellationToken>()).Returns(returnTrip);
                    tripClient.GetTripSnapshotAsync(
                            returnTripId,
                            Arg.Any<DateTimeOffset>(),
                            Arg.Any<CancellationToken>())
                        .Returns(returnTrip);
                    tripClient.LockRoundTripSeatsAsync(
                            default,
                            default!,
                            default,
                            default!,
                            default,
                            default!,
                            default,
                            default)
                        .ReturnsForAnyArgs(new LockRoundTripSeatsOutcome.Success(
                            new RoundTripSeatLockResult(tripId, Guid.NewGuid(), ["A01"], Now.AddMinutes(10)),
                            new RoundTripSeatLockResult(returnTripId, Guid.NewGuid(), ["B01"], Now.AddMinutes(10))));
                    paymentClient.BatchChargeAsync(default, default!, default!, default!, default)
                        .ReturnsForAnyArgs(call =>
                        {
                            var items = call.Arg<IReadOnlyList<BatchChargeItem>>();
                            return new BatchChargeOutcome.Success(items.Select(item =>
                                new BatchChargePaymentResult(
                                    Guid.NewGuid(),
                                    "BOOKING",
                                    item.ReferenceId,
                                    "SUCCEEDED",
                                    null)).ToArray());
                        });
                    var handler = new CreateRoundTripBookingCommandHandler(
                        bookings,
                        tripClient,
                        paymentClient,
                        bookingService,
                        voucherService,
                        voucherRepository,
                        outbox,
                        clock,
                        NullLogger<CreateRoundTripBookingCommandHandler>.Instance,
                        statusHistory,
                        canonicalizer);
                    var command = new CreateRoundTripBookingCommand(
                        passengerUserId,
                        $"race-{iteration}-{Guid.NewGuid():N}",
                        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
                            tripId,
                            duplicateStationId,
                            null,
                            null,
                            null,
                            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("A01")]),
                        new CreateRoundTripBookingCommand.RoundTripBookingLegCommand(
                            returnTripId,
                            duplicateStationId,
                            null,
                            null,
                            null,
                            [new CreateRoundTripBookingCommand.RoundTripSeatRequest("B01")]),
                        null,
                        "WALLET");
                    _ = await unitOfWork.ExecuteInTransactionAsync(
                        () => handler.Handle(command, CancellationToken.None),
                        CancellationToken.None);
                    break;
                }

            case StationWriter.EditPickup:
                {
                    var handler = new EditPickupCommandHandler(bookings, tripClient, clock, canonicalizer);
                    var command = new EditPickupCommand(
                        editableBookingId!.Value,
                        passengerUserId,
                        $"race-{iteration}",
                        duplicateStationId,
                        null,
                        "WALLET");
                    _ = await unitOfWork.ExecuteInTransactionAsync(
                        () => handler.Handle(command, CancellationToken.None),
                        CancellationToken.None);
                    break;
                }

            case StationWriter.EditDropoff:
                {
                    trip = trip with
                    {
                        OriginStation = new TripStationSnapshot(Guid.NewGuid(), "Origin"),
                        DestinationStation = new TripStationSnapshot(duplicateStationId, "Destination"),
                    };
                    tripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);
                    var handler = new EditDropoffCommandHandler(bookings, tripClient, clock, canonicalizer);
                    var command = new EditDropoffCommand(
                        editableBookingId!.Value,
                        passengerUserId,
                        $"race-{iteration}",
                        duplicateStationId,
                        null);
                    _ = await unitOfWork.ExecuteInTransactionAsync(
                        () => handler.Handle(command, CancellationToken.None),
                        CancellationToken.None);
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(writerKind), writerKind, null);
        }
    }

    private static TripSnapshot CreateTripSnapshot(
        Guid tripId,
        Guid operatorId,
        Guid originStationId,
        Guid destinationStationId,
        int departureOffsetHours = 6,
        Guid? returnRouteId = null)
        => new(
            tripId,
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            Now.AddHours(departureOffsetHours),
            Now.AddHours(departureOffsetHours + 2),
            100_000,
            new TripStationSnapshot(originStationId, "Origin"),
            new TripStationSnapshot(destinationStationId, "Destination"),
            [],
            new TripSeatSummary(40, 40),
            returnRouteId);

    private static async Task<Guid> SeedTerminalBookingAsync(
        TestDatabase database,
        Guid duplicateStationId,
        Guid passengerUserId,
        Guid tripId)
    {
        await using var db = database.CreateDbContext();
        var booking = CreateBooking(duplicateStationId, passengerUserId, tripId);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await SetStatusAsync(db, booking.Id, BookingStatus.COMPLETED);
        return booking.Id;
    }

    private static async Task<Guid> SeedConfirmedBookingAsync(
        TestDatabase database,
        Guid duplicateStationId,
        Guid passengerUserId,
        Guid tripId,
        StationWriter writerKind)
    {
        await using var db = database.CreateDbContext();
        var pickupStationId = writerKind == StationWriter.EditPickup ? Guid.NewGuid() : Guid.NewGuid();
        var booking = CreateBooking(
            pickupStationId,
            passengerUserId,
            tripId,
            writerKind == StationWriter.EditDropoff ? null : duplicateStationId);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await SetStatusAsync(db, booking.Id, BookingStatus.CONFIRMED);
        return booking.Id;
    }

    private static BookingEntity CreateBooking(
        Guid pickupStationId,
        Guid passengerUserId,
        Guid tripId,
        Guid? dropoffStationId = null)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Generate(Now.AddTicks(Random.Shared.Next(1, int.MaxValue))),
            passengerUserId,
            tripId,
            Guid.NewGuid(),
            pickupStationId,
            null,
            dropoffStationId,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            "Origin",
            "Destination",
            Now.AddHours(6));

    private static async Task SetStatusAsync(
        BookingDbContext db,
        Guid bookingId,
        BookingStatus status)
        => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_booking.bookings
            SET status = CAST({status.ToString()} AS public.booking_status)
            WHERE id = {bookingId};
            """);

    private static Task<bool> RedirectTableExistsAsync(BookingDbContext db)
        => db.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('vietride_booking.booking_station_redirects') IS NOT NULL AS \"Value\"")
            .SingleAsync();

    private static IBookingRepository CreateBookingRepository(BookingDbContext db)
        => CreateInternal<IBookingRepository>(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingRepository",
            db);

    private static IBookingStationRedirectRepository CreateRedirectRepository(
        BookingDbContext db,
        IClock clock)
        => CreateInternal<IBookingStationRedirectRepository>(
            "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingStationRedirectRepository",
            db,
            CreateBookingRepository(db),
            clock);

    private static IBookingStationCanonicalizer CreateCanonicalizer(BookingDbContext db)
        => CreateInternal<IBookingStationCanonicalizer>(
            "VietRide.Booking.Infrastructure.Services.BookingStationCanonicalizer",
            db);

    private static T CreateInternal<T>(string typeName, params object[] arguments)
    {
        var type = typeof(BookingDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (T)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: null)!;
    }

    public enum StationWriter
    {
        CreateBooking,
        CreateRoundTripBooking,
        EditPickup,
        EditDropoff,
    }

    private sealed class BeforeAdvisoryLockBarrier : IBookingStationCanonicalizer
    {
        private readonly IBookingStationCanonicalizer _inner;
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BeforeAdvisoryLockBarrier(IBookingStationCanonicalizer inner)
        {
            _inner = inner;
        }

        public Task Arrived => _arrived.Task;

        public void Release() => _release.TrySetResult();

        public async Task<StationCanonicalizationResult> LockAndResolveAsync(
            IReadOnlyCollection<Guid> stationIds,
            CancellationToken cancellationToken = default)
        {
            _arrived.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await _inner.LockAndResolveAsync(stationIds, cancellationToken);
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _databaseName;
        private readonly NpgsqlDataSource _dataSource;

        private TestDatabase(
            string connectionString,
            string databaseName,
            NpgsqlDataSource dataSource)
        {
            _connectionString = connectionString;
            _databaseName = databaseName;
            _dataSource = dataSource;
        }

        public static async Task<TestDatabase> CreateAsync(string prefix)
        {
            var databaseName = $"vietride_booking_{prefix}_{Guid.NewGuid():N}";
            var connectionString = BuildConnectionString(databaseName);
            await CreateDatabaseAsync(connectionString, databaseName);
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            BookingDbContext.ConfigurePostgresTypes(builder);
            var dataSource = builder.Build();
            var database = new TestDatabase(connectionString, databaseName, dataSource);
            await using var db = database.CreateDbContext();
            await db.Database.MigrateAsync();
            return database;
        }

        public BookingDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<BookingDbContext>()
                .UseNpgsql(_dataSource, npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    BookingDbContext.SchemaName))
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;
            return new BookingDbContext(options, new FixedClock(Now));
        }

        public async ValueTask DisposeAsync()
        {
            await _dataSource.DisposeAsync();
            await DropDatabaseAsync(_connectionString, _databaseName);
        }

        private static string BuildConnectionString(string databaseName)
        {
            const string fallback =
                "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
            var configured = Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING");
            var template = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
            var connectionString = template.Replace(
                "{databaseName}",
                databaseName,
                StringComparison.OrdinalIgnoreCase);
            return new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = databaseName,
            }.ConnectionString;
        }

        private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
        {
            var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres",
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(maintenance);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task DropDatabaseAsync(string connectionString, string databaseName)
        {
            var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres",
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(maintenance);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
