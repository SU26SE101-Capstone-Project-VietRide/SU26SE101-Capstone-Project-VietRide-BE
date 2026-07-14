using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.DriverTrips;

public sealed class StartTripCommandHandlerTests
{
    [Fact]
    public async Task Handle_AssignedDriver_StartsAndEnqueuesOneEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var trip = CreateTrip();
        trip.MarkBoarding(now.AddMinutes(-5));
        var fixture = new Fixture(trip, now);

        var response = await fixture.Handler.Handle(
            new StartTripCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        response.Status.Should().Be("IN_PROGRESS");
        response.ActualDepartureTime.Should().Be(now);
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events[0].EventType.Should().Be("trip.trip.started");
        fixture.Outbox.Events[0].Payload.Should().Contain(trip.Id.ToString());
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MismatchedDriver_ThrowsForbiddenAndRollsBack()
    {
        var now = DateTimeOffset.UtcNow;
        var trip = CreateTrip();
        trip.MarkBoarding(now.AddMinutes(-5));
        var fixture = new Fixture(trip, now);

        var action = () => fixture.Handler.Handle(
            new StartTripCommand(trip.Id, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidState_ThrowsConflictAndRollsBack()
    {
        var trip = CreateTrip();
        var fixture = new Fixture(trip, DateTimeOffset.UtcNow);

        var action = () => fixture.Handler.Handle(
            new StartTripCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedConflictException>();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip()
    {
        var departure = DateTimeOffset.UtcNow;
        return VietRide.Trip.Domain.Entities.Trip.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            departure, departure.AddHours(4), TripSource.MANUAL, Money.FromRaw(100_000), 500m, 5m);
    }

    private sealed class Fixture
    {
        public Fixture(VietRide.Trip.Domain.Entities.Trip trip, DateTimeOffset now)
        {
            Outbox = new RecordingOutbox();
            UnitOfWork = new RecordingUnitOfWork();
            Handler = new StartTripCommandHandler(
                new LifecycleTripRepository(trip),
                Outbox,
                UnitOfWork,
                new FrozenClock(now));
        }

        public RecordingOutbox Outbox { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public StartTripCommandHandler Handler { get; }
    }

    private sealed class LifecycleTripRepository : ITripRepository
    {
        private readonly VietRide.Trip.Domain.Entities.Trip trip;

        public LifecycleTripRepository(VietRide.Trip.Domain.Entities.Trip trip) => this.trip = trip;

        public Task<VietRide.Trip.Domain.Entities.Trip?> AcquireForLifecycleTransitionAsync(Guid tripId, CancellationToken cancellationToken) =>
            Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == tripId ? trip : null);

        public Task<VietRide.Trip.Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == id ? trip : null);

        public Task<VietRide.Trip.Domain.Entities.Trip> AddAsync(VietRide.Trip.Domain.Entities.Trip entity, CancellationToken cancellationToken = default) =>
            Task.FromResult(entity);

        public void Update(VietRide.Trip.Domain.Entities.Trip entity) { }
        public void Remove(VietRide.Trip.Domain.Entities.Trip entity) { }
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> Query() => new[] { trip }.AsQueryable();
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> QueryNoTracking() => Query();
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payload, CancellationToken cancellationToken = default)
        {
            Events.Add((eventType, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default) =>
            operation();
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
