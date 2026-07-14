using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.DriverTrips;

public sealed class CompleteTripCommandHandlerTests
{
    [Theory]
    [InlineData("DRIVER")]
    [InlineData("ASSISTANT")]
    public async Task Handle_AssignedCrew_CompletesWithAuditAndEvent(string role)
    {
        var now = DateTimeOffset.UtcNow;
        var trip = CreateInProgressTrip(now);
        var actor = role == "DRIVER" ? trip.DriverUserId : trip.AssistantUserId!.Value;
        var fixture = new Fixture(trip, now);

        var response = await fixture.Handler.Handle(
            new CompleteTripCommand(trip.Id, actor, role),
            CancellationToken.None);

        response.Status.Should().Be("COMPLETED");
        response.CompletedByUserId.Should().Be(actor);
        fixture.Audit.Logs.Should().ContainSingle(log =>
            log.TripId == trip.Id
            && log.ActorUserId == actor
            && log.Action == TripAuditAction.TripCompletedManual
            && log.OccurredAt == now);
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events[0].EventType.Should().Be("trip.trip.completed");
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Theory]
    [InlineData("DRIVER")]
    [InlineData("ASSISTANT")]
    [InlineData("PASSENGER")]
    public async Task Handle_MismatchOrUnsupportedRole_ThrowsForbidden(string role)
    {
        var now = DateTimeOffset.UtcNow;
        var trip = CreateInProgressTrip(now);
        var fixture = new Fixture(trip, now);

        var action = () => fixture.Handler.Handle(
            new CompleteTripCommand(trip.Id, Guid.NewGuid(), role),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Audit.Logs.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidState_ThrowsConflictWithoutSideEffects()
    {
        var now = DateTimeOffset.UtcNow;
        var trip = CreateTrip(now);
        trip.MarkBoarding(now);
        var fixture = new Fixture(trip, now);

        var action = () => fixture.Handler.Handle(
            new CompleteTripCommand(trip.Id, trip.DriverUserId, "DRIVER"),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedConflictException>();
        fixture.Audit.Logs.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateInProgressTrip(DateTimeOffset now)
    {
        var trip = CreateTrip(now);
        trip.MarkBoarding(now.AddMinutes(-10));
        trip.Start(now.AddMinutes(-5));
        return trip;
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(DateTimeOffset departure)
        => VietRide.Trip.Domain.Entities.Trip.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            departure, departure.AddHours(4), TripSource.MANUAL, Money.FromRaw(100_000), 500m, 5m);

    private sealed class Fixture
    {
        public Fixture(VietRide.Trip.Domain.Entities.Trip trip, DateTimeOffset now)
        {
            Audit = new RecordingAuditRepository();
            Outbox = new RecordingOutbox();
            UnitOfWork = new RecordingUnitOfWork();
            Handler = new CompleteTripCommandHandler(
                new LifecycleTripRepository(trip),
                Audit,
                Outbox,
                UnitOfWork,
                new FrozenClock(now));
        }

        public RecordingAuditRepository Audit { get; }
        public RecordingOutbox Outbox { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public CompleteTripCommandHandler Handler { get; }
    }

    private sealed class LifecycleTripRepository : ITripRepository
    {
        private readonly VietRide.Trip.Domain.Entities.Trip trip;
        public LifecycleTripRepository(VietRide.Trip.Domain.Entities.Trip trip) => this.trip = trip;
        public Task<VietRide.Trip.Domain.Entities.Trip?> AcquireForLifecycleTransitionAsync(Guid tripId, CancellationToken cancellationToken) => Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == tripId ? trip : null);
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == id ? trip : null);
        public Task<VietRide.Trip.Domain.Entities.Trip> AddAsync(VietRide.Trip.Domain.Entities.Trip entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public void Update(VietRide.Trip.Domain.Entities.Trip entity) { }
        public void Remove(VietRide.Trip.Domain.Entities.Trip entity) { }
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> Query() => new[] { trip }.AsQueryable();
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> QueryNoTracking() => Query();
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class RecordingAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Logs { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Logs.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TripAuditLog>>(Logs.Where(log => log.TripId == tripId).ToArray());
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
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task CommitAsync(CancellationToken cancellationToken = default) { CommitCount++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken cancellationToken = default) { RollbackCount++; return Task.CompletedTask; }
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default) => operation();
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
