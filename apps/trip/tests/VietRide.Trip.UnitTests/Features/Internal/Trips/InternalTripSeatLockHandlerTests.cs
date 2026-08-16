using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Application.Features.Internal.Trips.BookRoundTripSeats;
using VietRide.Trip.Application.Features.Internal.Trips.BookSeats;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseExpiredSeatLocks;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;
using VietRide.Trip.Application.Features.Internal.Trips.Requests;
using VietRide.Trip.Domain.Entities;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;


namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class InternalTripSeatLockHandlerTests
{
    [Fact]
    public async Task LockSeats_AvailableSeat_HoldsSeatAndReturnsToken()
    {
        var fixture = Fixture.Create();

        var result = await fixture.LockHandler.Handle(
            new LockSeatsCommand(fixture.Trip.Id, ["A01"], Guid.NewGuid(), 60, Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        result.LockedSeats.Should().ContainSingle("A01");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.HELD);
        fixture.UnitOfWork.SaveChangesCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LockSeats_WhenSaveFails_ReleasesAcquiredRedisLock()
    {
        var fixture = Fixture.Create();
        fixture.UnitOfWork.FailOnSave = true;

        var action = () => fixture.LockHandler.Handle(
            new LockSeatsCommand(fixture.Trip.Id, ["A01"], Guid.NewGuid(), 60, Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.SeatLocks.IsLockedAsync(fixture.Trip.Id, "A01")).Should().BeFalse();
    }

    [Fact]
    public void RequireIdempotencyKey_MissingHeader_ThrowsCodedValidationException()
    {
        var filter = new RequireIdempotencyKeyAttribute();
        var context = CreateActionExecutingContext(new DefaultHttpContext());

        var action = () => filter.OnActionExecuting(context);

        var exception = action.Should().Throw<CodedValidationException>().Which;
        exception.ErrorCode.Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        exception.Errors.Should().ContainSingle(error => error.Field == "Idempotency-Key");
    }

    [Fact]
    public async Task LockSeats_Success_ReturnsApiResponseEnvelope()
    {
        var lockResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(10));
        var mediator = new CapturingMediator(lockResult);
        var controller = CreateController(mediator);

        var response = await controller.LockSeatsAsync(
            Guid.NewGuid(),
            new LockSeatsRequest(["A01"], Guid.NewGuid(), 60),
            CancellationToken.None);

        var result = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = result.Value.Should().BeOfType<ApiResponse<LockSeatsResult>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().Be(lockResult);
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task BookSeats_WrongToken_ThrowsSeatUnavailableWithoutBooking()
    {
        var fixture = Fixture.Create(held: true);
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", Guid.NewGuid().ToString("D"));

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("BOOKING_SEAT_UNAVAILABLE");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.HELD);
    }

    [Fact]
    public async Task BookSeats_RetryAfterSuccessAfterLockConsumed_ReturnsNoOp()
    {
        var fixture = Fixture.Create(held: true);
        var seatLockToken = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", seatLockToken.ToString("D"));

        await fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                seatLockToken,
                bookingId,
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);

        await fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                seatLockToken,
                bookingId,
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);
        (await fixture.SeatLocks.IsLockedAsync(fixture.Trip.Id, "A01")).Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task BookSeats_RetryFromDifferentBooking_DoesNotTakeOwnership()
    {
        var fixture = Fixture.Create(held: true);
        var seatLockToken = Guid.NewGuid();
        var firstBookingId = Guid.NewGuid();
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", seatLockToken.ToString("D"));
        await fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                seatLockToken,
                firstBookingId,
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                seatLockToken,
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedConflictException>();
        fixture.Seats.Single().BookingId.Should().Be(firstBookingId);
    }

    [Fact]
    public async Task BookRoundTripSeats_PersistsEachLegBookingOwner()
    {
        var outboundFixture = Fixture.Create(held: true);
        var returnFixture = Fixture.Create(held: true);
        var outboundToken = Guid.NewGuid();
        var returnToken = Guid.NewGuid();
        var outboundBookingId = Guid.NewGuid();
        var returnBookingId = Guid.NewGuid();
        var locks = new InMemorySeatLockStore();
        locks.Lock(outboundFixture.Trip.Id, "A01", outboundToken.ToString("D"));
        locks.Lock(returnFixture.Trip.Id, "A01", returnToken.ToString("D"));
        var unitOfWork = new StubUnitOfWork();
        var handler = new BookRoundTripSeatsHandler(
            new StubTripRepository([outboundFixture.Trip, returnFixture.Trip]),
            new StubTripSeatRepository([outboundFixture.Seats.Single(), returnFixture.Seats.Single()]),
            locks,
            unitOfWork);

        await handler.Handle(
            new BookRoundTripSeatsCommand(
                new BookRoundTripSeatsLeg(
                    outboundFixture.Trip.Id,
                    outboundToken,
                    outboundBookingId,
                    [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
                new BookRoundTripSeatsLeg(
                    returnFixture.Trip.Id,
                    returnToken,
                    returnBookingId,
                    [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")])),
            CancellationToken.None);

        outboundFixture.Seats.Single().BookingId.Should().Be(outboundBookingId);
        returnFixture.Seats.Single().BookingId.Should().Be(returnBookingId);
        (await locks.IsLockedAsync(outboundFixture.Trip.Id, "A01")).Should().BeFalse();
        (await locks.IsLockedAsync(returnFixture.Trip.Id, "A01")).Should().BeFalse();
        unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task BookSeats_BookedSeatWithWrongToken_ThrowsSeatUnavailable()
    {
        var fixture = Fixture.Create(booked: true);
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", Guid.NewGuid().ToString("D"));

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("BOOKING_SEAT_UNAVAILABLE");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);
    }

    [Fact]
    public async Task BookSeats_BookedSeatWithMissingExpiredMarker_ThrowsSeatUnavailable()
    {
        var fixture = Fixture.Create(booked: true);

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("BOOKING_SEAT_UNAVAILABLE");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);
    }

    [Fact]
    public async Task BookSeats_MixedBookedAndHeldSeats_ThrowsSeatUnavailable()
    {
        var fixture = Fixture.Create(booked: true);
        var heldSeat = TripSeat.Create(fixture.Trip.Id, "A02");
        heldSeat.MarkHeld();
        fixture.Seats.Add(heldSeat);
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A02", Guid.NewGuid().ToString("D"));

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [
                    new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01"),
                    new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A02"),
                ]),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("BOOKING_SEAT_UNAVAILABLE");
        fixture.Seats.Select(seat => seat.Status).Should().Equal(TripSeatStatus.BOOKED, TripSeatStatus.HELD);
    }

    [Fact]
    public async Task BookSeats_SaveConsumesRedisMarker()
    {
        var fixture = Fixture.Create(held: true);
        var token = Guid.NewGuid();
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", token.ToString("D"));

        await fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                token,
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);
        (await fixture.SeatLocks.IsOwnedByAsync(fixture.Trip.Id, "A01", token.ToString("D"))).Should().BeFalse();
        fixture.SeatLocks.ReleaseCallCount.Should().Be(1);
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task BookSeats_MissingExpiredKey_ThrowsSeatUnavailableWithoutBooking()
    {
        var fixture = Fixture.Create(held: true);

        var action = () => fixture.BookHandler.Handle(
            new BookSeatsCommand(
                fixture.Trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [new PassengerSeatAssignmentRequest(Guid.NewGuid(), "A01")]),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("BOOKING_SEAT_UNAVAILABLE");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.HELD);
    }

    [Fact]
    public async Task LockSeats_ExpiredHeldSeat_ReconcilesAndLocksAgain()
    {
        var fixture = Fixture.Create(held: true);

        var result = await fixture.LockHandler.Handle(
            new LockSeatsCommand(fixture.Trip.Id, ["A01"], Guid.NewGuid(), 60, Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        result.LockedSeats.Should().ContainSingle("A01");
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.HELD);
    }

    [Fact]
    public async Task ReleaseSeats_SaveSucceeds_LogsRedisFailureAndStillReleasesDb()
    {
        var fixture = Fixture.Create(held: true);
        var token = Guid.NewGuid();
        fixture.SeatLocks.Lock(fixture.Trip.Id, "A01", token.ToString("D"));
        fixture.SeatLocks.ThrowOnRelease = true;

        await fixture.ReleaseHandler.Handle(
            new ReleaseSeatsCommand(fixture.Trip.Id, token, ["A01"]),
            CancellationToken.None);

        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.AVAILABLE);
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
        fixture.SeatLocks.ReleaseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReleaseSeats_BookedSeatWithExpiredRedisMarker_DoesNotReleaseBookedSeat()
    {
        var fixture = Fixture.Create(booked: true);

        await fixture.ReleaseHandler.Handle(
            new ReleaseSeatsCommand(fixture.Trip.Id, Guid.NewGuid(), ["A01"]),
            CancellationToken.None);

        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.BOOKED);
        fixture.Seats.Single().BookingId.Should().NotBeNull();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
        fixture.SeatLocks.ReleaseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ReleaseExpiredSeatLocks_StaleHeldSeat_ReleasesToAvailable()
    {
        var fixture = Fixture.Create(held: true);

        var released = await fixture.ReleaseExpiredHandler.Handle(new ReleaseExpiredSeatLocksCommand(), CancellationToken.None);

        released.Should().Be(1);
        fixture.Seats.Single().Status.Should().Be(TripSeatStatus.AVAILABLE);
    }

    [Fact]
    public async Task LockSeats_ConcurrentSameSeat_HasExactlyOneWinner()
    {
        var fixture = Fixture.Create();
        var commands = Enumerable.Range(0, 2)
            .Select(_ => fixture.LockHandler.Handle(
                new LockSeatsCommand(fixture.Trip.Id, ["A01"], Guid.NewGuid(), 60, Guid.NewGuid().ToString("D")),
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(commands.Select(task => task.ContinueWith(_ => { })));

        commands.Count(task => task.Status == TaskStatus.RanToCompletion).Should().Be(1);
    }

    [Fact]
    public async Task InMemoryIdempotency_StaleActorCannotCompleteNewerSameFingerprintReservation()
    {
        var store = new InMemorySeatLockIdempotencyStore();
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("D");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var staleReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var newerReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        var staleResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(1));

        var completed = await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            staleReservation.ReservationToken!,
            ["A01"],
            staleResult,
            TimeSpan.FromMinutes(15),
            CancellationToken.None);

        completed.Should().BeFalse();
        (await store.GetAsync(tripId, idempotencyKey)).Should().BeEquivalentTo(new SeatLockIdempotencyEntry(
            fingerprint,
            ["A01"],
            null,
            newerReservation.ReservationToken));
    }

    [Fact]
    public async Task InMemoryIdempotency_StaleCleanupCannotDeleteNewerCompletedReservation()
    {
        var store = new InMemorySeatLockIdempotencyStore();
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("D");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var staleReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var newerReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        var completedResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(1));
        await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            newerReservation.ReservationToken!,
            ["A01"],
            completedResult,
            TimeSpan.FromMinutes(15),
            CancellationToken.None);

        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);

        (await store.GetAsync(tripId, idempotencyKey)).Should().BeEquivalentTo(new SeatLockIdempotencyEntry(
            fingerprint,
            ["A01"],
            completedResult,
            newerReservation.ReservationToken));
    }

    private static InternalTripsController CreateController(IMediator mediator)
    {
        var controller = new InternalTripsController(mediator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return controller;
    }

    private static ActionExecutingContext CreateActionExecutingContext(HttpContext httpContext)
    {
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?>(),
            new object());
    }

    private sealed class Fixture
    {
        private Fixture(DomainTrip trip, List<TripSeat> seats)
        {
            Trip = trip;
            Seats = seats;
            UnitOfWork = new StubUnitOfWork();
            SeatLocks = new InMemorySeatLockStore();
            Idempotency = new InMemorySeatLockIdempotencyStore();
            var trips = new StubTripRepository([trip]);
            var tripSeats = new StubTripSeatRepository(seats);
            LockHandler = new LockSeatsHandler(trips, tripSeats, SeatLocks, Idempotency, new StubSeatLockTtlProvider(), UnitOfWork);
            BookHandler = new BookSeatsHandler(trips, tripSeats, SeatLocks, UnitOfWork);
            ReleaseExpiredHandler = new ReleaseExpiredSeatLocksHandler(new StubExpiredSeatLockReleaser(tripSeats, SeatLocks, UnitOfWork));
            ReleaseHandler = new ReleaseSeatsHandler(tripSeats, SeatLocks, UnitOfWork, NullLogger<ReleaseSeatsHandler>.Instance);
        }

        public DomainTrip Trip { get; }
        public List<TripSeat> Seats { get; }
        public StubUnitOfWork UnitOfWork { get; }
        public InMemorySeatLockStore SeatLocks { get; }
        public InMemorySeatLockIdempotencyStore Idempotency { get; }
        public LockSeatsHandler LockHandler { get; }
        public BookSeatsHandler BookHandler { get; }
        public ReleaseSeatsHandler ReleaseHandler { get; }
        public ReleaseExpiredSeatLocksHandler ReleaseExpiredHandler { get; }

        public static Fixture Create(bool held = false, bool booked = false)
        {
            var trip = DomainTrip.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddHours(3),
                TripSource.MANUAL,
                Money.FromRaw(100000),
                null,
                0m);
            var seat = TripSeat.Create(trip.Id, "A01");
            if (held)
            {
                seat.MarkHeld();
            }
            else if (booked)
            {
                seat.MarkHeld();
                seat.MarkBooked(Guid.NewGuid());
            }

            return new Fixture(trip, [seat]);
        }
    }

    private sealed class StubSeatLockTtlProvider : ISeatLockTtlProvider
    {
        public TimeSpan DefaultTtl => TimeSpan.FromMinutes(10);
    }

    private sealed class InMemorySeatLockIdempotencyStore : ISeatLockIdempotencyStore
    {
        private readonly Dictionary<(Guid TripId, string Key), SeatLockIdempotencyEntry> entries = new();

        public SeatLockIdempotencyEntry? CollisionEntry { get; set; }
        public TimeSpan? LastTryReserveTtl { get; private set; }
        public TimeSpan? LastStoreCompletedTtl { get; private set; }
        public bool FailStoreCompleted { get; set; }

        public Task<SeatLockIdempotencyEntry?> GetAsync(Guid tripId, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            entries.TryGetValue((tripId, idempotencyKey), out var entry);
            return Task.FromResult(entry);
        }

        public Task<SeatLockIdempotencyReservation> TryReserveAsync(
            Guid tripId,
            string idempotencyKey,
            string requestFingerprint,
            IReadOnlyCollection<string> normalizedSeatNumbers,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            LastTryReserveTtl = ttl;
            if (CollisionEntry is { } collisionEntry)
            {
                entries[(tripId, idempotencyKey)] = collisionEntry;
                CollisionEntry = null;
                return Task.FromResult(new SeatLockIdempotencyReservation(false, null, collisionEntry));
            }

            if (entries.TryGetValue((tripId, idempotencyKey), out var current))
            {
                return Task.FromResult(new SeatLockIdempotencyReservation(false, null, current));
            }

            var reservationToken = Guid.NewGuid().ToString("D");
            var entry = new SeatLockIdempotencyEntry(requestFingerprint, normalizedSeatNumbers.ToArray(), null, reservationToken);
            entries[(tripId, idempotencyKey)] = entry;
            return Task.FromResult(new SeatLockIdempotencyReservation(true, reservationToken, null));
        }

        public Task<bool> StoreCompletedAsync(
            Guid tripId,
            string idempotencyKey,
            string requestFingerprint,
            string expectedReservationToken,
            IReadOnlyCollection<string> normalizedSeatNumbers,
            LockSeatsResult result,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            LastStoreCompletedTtl = ttl;
            if (FailStoreCompleted)
            {
                return Task.FromResult(false);
            }

            if (entries.TryGetValue((tripId, idempotencyKey), out var current) &&
                string.Equals(current.RequestFingerprint, requestFingerprint, StringComparison.Ordinal) &&
                string.Equals(current.ReservationToken, expectedReservationToken, StringComparison.Ordinal) &&
                !current.IsCompleted)
            {
                entries[(tripId, idempotencyKey)] = new SeatLockIdempotencyEntry(
                    requestFingerprint,
                    normalizedSeatNumbers.ToArray(),
                    result,
                    expectedReservationToken);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task RemoveReservationAsync(Guid tripId, string idempotencyKey, string expectedReservationToken, CancellationToken cancellationToken = default)
        {
            if (entries.TryGetValue((tripId, idempotencyKey), out var current) &&
                string.Equals(current.ReservationToken, expectedReservationToken, StringComparison.Ordinal) &&
                !current.IsCompleted)
            {
                entries.Remove((tripId, idempotencyKey));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubExpiredSeatLockReleaser : IExpiredSeatLockReleaser
    {
        private readonly ITripSeatRepository tripSeatRepository;
        private readonly ISeatLockStore seatLockStore;
        private readonly IUnitOfWork unitOfWork;

        public StubExpiredSeatLockReleaser(ITripSeatRepository tripSeatRepository, ISeatLockStore seatLockStore, IUnitOfWork unitOfWork)
        {
            this.tripSeatRepository = tripSeatRepository;
            this.seatLockStore = seatLockStore;
            this.unitOfWork = unitOfWork;
        }

        public async Task<int> ReleaseExpiredAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            var released = 0;
            foreach (var seat in tripSeatRepository.Query().Where(seat => seat.Status == TripSeatStatus.HELD))
            {
                if (!await seatLockStore.IsLockedAsync(seat.TripId, seat.SeatNumber, cancellationToken))
                {
                    seat.Release();
                    released++;
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return released;
        }
    }

    private sealed class StubTripRepository : StubRepository<DomainTrip, Guid>, ITripRepository
    {
        public StubTripRepository(List<DomainTrip> items) : base(items, item => item.Id) { }

        public Task<DomainTrip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class StubTripSeatRepository : StubRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public StubTripSeatRepository(List<TripSeat> items) : base(items, item => item.Id) { }
    }

    private class StubRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly List<TEntity> items;
        private readonly Func<TEntity, TId> idSelector;

        protected StubRepository(List<TEntity> items, Func<TEntity, TId> idSelector)
        {
            this.items = items;
            this.idSelector = idSelector;
        }

        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct) =>
            Task.FromResult(items.FirstOrDefault(item => EqualityComparer<TId>.Default.Equals(idSelector(item), id)));

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken ct)
        {
            items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity) { }

        public void Remove(TEntity entity) => items.Remove(entity);

        public IQueryable<TEntity> Query() => items.AsQueryable();

        public IQueryable<TEntity> QueryNoTracking() => items.AsQueryable();
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public bool FailOnSave { get; set; }
        public int SaveChangesCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveChangesCount++;
            if (FailOnSave)
            {
                throw new InvalidOperationException("Save failed.");
            }

            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly object? response;

        public CapturingMediator(object? response = null)
        {
            this.response = response;
        }

        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(response is TResponse typedResponse ? typedResponse : default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult<object?>(null);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            EmptyStream<object?>();

        private static async IAsyncEnumerable<T> EmptyStream<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class InMemorySeatLockStore : ISeatLockStore
    {
        private readonly Dictionary<(Guid TripId, string SeatNumber), string> locks = new();
        private readonly object gate = new();

        public bool ThrowOnRelease { get; set; }
        public int ReleaseCallCount { get; private set; }

        public Task<bool> TryAcquireAsync(Guid tripId, IReadOnlyCollection<string> seatNumbers, string lockOwner, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (seatNumbers.Any(seatNumber => locks.ContainsKey((tripId, seatNumber))))
                {
                    return Task.FromResult(false);
                }

                foreach (var seatNumber in seatNumbers)
                {
                    locks[(tripId, seatNumber)] = lockOwner;
                }

                return Task.FromResult(true);
            }
        }

        public Task ReleaseAsync(Guid tripId, IReadOnlyCollection<string> seatNumbers, string lockOwner, CancellationToken cancellationToken = default)
        {
            ReleaseCallCount++;
            if (ThrowOnRelease)
            {
                throw new InvalidOperationException("Redis release failed.");
            }

            foreach (var seatNumber in seatNumbers)
            {
                if (locks.TryGetValue((tripId, seatNumber), out var owner) && owner == lockOwner)
                {
                    locks.Remove((tripId, seatNumber));
                }
            }

            return Task.CompletedTask;
        }

        public Task<bool> IsLockedAsync(Guid tripId, string seatNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(locks.ContainsKey((tripId, seatNumber)));

        public Task<bool> IsOwnedByAsync(Guid tripId, string seatNumber, string lockOwner, CancellationToken cancellationToken = default) =>
            Task.FromResult(locks.TryGetValue((tripId, seatNumber), out var owner) && owner == lockOwner);

        public void Lock(Guid tripId, string seatNumber, string owner) => locks[(tripId, seatNumber)] = owner;
    }
}
