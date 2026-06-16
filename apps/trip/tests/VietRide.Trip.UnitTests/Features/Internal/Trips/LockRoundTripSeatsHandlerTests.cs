using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class LockRoundTripSeatsHandlerTests
{
    [Fact]
    public async Task Handle_WithAvailableScheduledTrips_HoldsBothLegs()
    {
        var outbound = BuildTrip(["A01"]);
        var @return = BuildTrip(["B01"]);
        var store = new FakeSeatLockStore();
        var handler = BuildHandler(outbound, @return, store);

        var result = await handler.Handle(Command(outbound.Id, ["A01"], @return.Id, ["B01"]), CancellationToken.None);

        Assert.Equal(["A01"], result.Outbound.LockedSeats);
        Assert.Equal(["B01"], result.Return.LockedSeats);
        Assert.All(outbound.Seats.Concat(@return.Seats), seat => Assert.Equal(TripSeatStatus.HELD, seat.Status));
        Assert.False(store.Released);
    }

    [Fact]
    public async Task Handle_WhenTripMissing_ThrowsTripNotFound_AndDoesNotSetRedisLocks()
    {
        var @return = BuildTrip(["B01"]);
        var store = new FakeSeatLockStore();
        var handler = BuildHandler(null, @return, store);

        var exception = await Assert.ThrowsAsync<CodedNotFoundException>(() => handler.Handle(
            Command(Guid.NewGuid(), ["A01"], @return.Id, ["B01"]),
            CancellationToken.None));

        Assert.Equal("TRIP_NOT_FOUND", exception.ErrorCode);
        Assert.False(store.Released);
    }

    [Fact]
    public async Task Handle_WhenTripNotBookable_ThrowsConflict_AndDoesNotSetRedisLocks()
    {
        var outbound = BuildTrip(["A01"], TripStatus.BOARDING);
        var @return = BuildTrip(["B01"]);
        var store = new FakeSeatLockStore();
        var handler = BuildHandler(outbound, @return, store);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            Command(outbound.Id, ["A01"], @return.Id, ["B01"]),
            CancellationToken.None));

        Assert.Equal("BOOKING_TRIP_NOT_BOOKABLE", exception.ErrorCode);
        Assert.False(store.Released);
    }

    [Fact]
    public async Task Handle_WhenSeatUnavailable_ThrowsConflict_AndDoesNotPartiallyHold()
    {
        var outbound = BuildTrip(["A01"]);
        var @return = BuildTrip(["B01"]);
        @return.HoldSeats(["B01"]);
        var store = new FakeSeatLockStore();
        var handler = BuildHandler(outbound, @return, store);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            Command(outbound.Id, ["A01"], @return.Id, ["B01"]),
            CancellationToken.None));

        Assert.Equal("BOOKING_SEAT_UNAVAILABLE", exception.ErrorCode);
        Assert.Contains(exception.Errors, field => field.Message == "B01");
        Assert.Equal(TripSeatStatus.AVAILABLE, outbound.Seats.Single().Status);
        Assert.True(store.Released);
    }

    [Fact]
    public async Task Handle_WhenRedisReportsUnavailable_ThrowsConflict_AndDoesNotHoldSeats()
    {
        var outbound = BuildTrip(["A01"]);
        var @return = BuildTrip(["B01"]);
        var store = new FakeSeatLockStore(["A01"]);
        var handler = BuildHandler(outbound, @return, store);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            Command(outbound.Id, ["A01"], @return.Id, ["B01"]),
            CancellationToken.None));

        Assert.Equal("BOOKING_SEAT_UNAVAILABLE", exception.ErrorCode);
        Assert.All(outbound.Seats.Concat(@return.Seats), seat => Assert.Equal(TripSeatStatus.AVAILABLE, seat.Status));
        Assert.False(store.Released);
    }

    [Fact]
    public async Task Handle_WhenIdempotentReplay_ReturnsStoredResult()
    {
        var outbound = BuildTrip(["A01"]);
        var @return = BuildTrip(["B01"]);
        var replay = new RoundTripSeatLockReplay(
            new RoundTripSeatLockReplayLeg(outbound.Id, Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(10)),
            new RoundTripSeatLockReplayLeg(@return.Id, Guid.NewGuid(), ["B01"], DateTimeOffset.UtcNow.AddMinutes(10)));
        var handler = BuildHandler(outbound, @return, new FakeSeatLockStore(replay: replay));

        var result = await handler.Handle(Command(outbound.Id, ["A01"], @return.Id, ["B01"]), CancellationToken.None);

        Assert.Equal(replay.Outbound.SeatLockToken, result.Outbound.SeatLockToken);
        Assert.All(outbound.Seats.Concat(@return.Seats), seat => Assert.Equal(TripSeatStatus.AVAILABLE, seat.Status));
    }

    private static LockRoundTripSeatsHandler BuildHandler(
        TripEntity? outbound,
        TripEntity? @return,
        IRoundTripSeatLockStore store)
        => new(
            new FakeTripRepository(outbound, @return),
            store,
            new FakeUnitOfWork());

    private static LockRoundTripSeatsCommand Command(
        Guid outboundTripId,
        IReadOnlyList<string> outboundSeats,
        Guid returnTripId,
        IReadOnlyList<string> returnSeats)
        => new(
            new LockRoundTripSeatsLegRequest(outboundTripId, outboundSeats),
            new LockRoundTripSeatsLegRequest(returnTripId, returnSeats),
            Guid.NewGuid(),
            600,
            "idem-1");

    private static TripEntity BuildTrip(IReadOnlyList<string> seats, TripStatus status = TripStatus.SCHEDULED)
    {
        var trip = TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(3),
            Money.FromRaw(100_000),
            TripSource.MANUAL);

        foreach (var seat in seats)
        {
            trip.AddSeat(TripSeat.Create(trip.Id, seat));
        }

        trip.ChangeStatus(status);
        return trip;
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        private readonly TripEntity? outbound;
        private readonly TripEntity? @return;

        public FakeTripRepository(TripEntity? outbound, TripEntity? @return)
        {
            this.outbound = outbound;
            this.@return = @return;
        }

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult(new[] { outbound, @return }.FirstOrDefault(trip => trip?.Id == tripId));

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) => GetWithSeatsAsync(id, ct);
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(TripEntity entity) { }
        public void Remove(TripEntity entity) { }
        public IQueryable<TripEntity> Query() => Array.Empty<TripEntity>().AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Array.Empty<TripEntity>().AsQueryable();
    }

    private sealed class FakeSeatLockStore : IRoundTripSeatLockStore
    {
        private readonly IReadOnlyList<string> unavailableSeats;
        private readonly RoundTripSeatLockReplay? replay;

        public FakeSeatLockStore(IReadOnlyList<string>? unavailableSeats = null, RoundTripSeatLockReplay? replay = null)
        {
            this.unavailableSeats = unavailableSeats ?? [];
            this.replay = replay;
        }

        public bool Released { get; private set; }

        public Task<RoundTripSeatLockStoreResult> TryLockAsync(RoundTripSeatLockStoreRequest request, CancellationToken cancellationToken)
            => Task.FromResult(replay is not null
                ? new RoundTripSeatLockStoreResult(true, true, [], replay)
                : new RoundTripSeatLockStoreResult(false, unavailableSeats.Count == 0, unavailableSeats, null));

        public Task ReleaseAsync(IReadOnlyList<RoundTripSeatLockKey> keys, string idempotencyKey, CancellationToken cancellationToken)
        {
            Released = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
