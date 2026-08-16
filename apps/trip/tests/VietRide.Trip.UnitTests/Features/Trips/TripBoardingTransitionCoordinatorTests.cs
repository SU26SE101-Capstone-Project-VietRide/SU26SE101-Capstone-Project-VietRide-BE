using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Trips;

public sealed class TripBoardingTransitionCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-17T08:00:00Z");

    [Fact]
    public async Task StartManual_AtInclusiveEarlyBoundary_BoardsAndWritesOneAuditAndEvent()
    {
        var trip = CreateTrip(Now.AddMinutes(180));
        var fixture = new Fixture(trip);

        var result = await fixture.Coordinator.StartManualAsync(
            trip.Id,
            trip.DriverUserId,
            "DRIVER",
            null,
            Now,
            CancellationToken.None);

        result.Should().Be(new TripBoardingTransitionResult(trip.Id, "BOARDING"));
        fixture.Outbox.Events.Should().ContainSingle(item => item.EventType == "trip.trip.boarding_started");
        fixture.Audits.Items.Should().ContainSingle(item =>
            item.Action == "TRIP_BOARDING_STARTED_MANUAL"
            && item.ActorUserId == trip.DriverUserId);
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task StartManual_OutsideEarlyBoundary_ReturnsSpecificConflict()
    {
        var trip = CreateTrip(Now.AddMinutes(180).AddTicks(1));
        var fixture = new Fixture(trip);

        var action = () => fixture.Coordinator.StartManualAsync(
            trip.Id,
            trip.DriverUserId,
            "DRIVER",
            null,
            Now,
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("TRIP_BOARDING_TOO_EARLY");
        trip.Status.Should().Be(TripStatus.SCHEDULED);
        fixture.Outbox.Events.Should().BeEmpty();
        fixture.Audits.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task StartManual_WhenAlreadyBoarding_IsNoOpWithoutDuplicateAuditOrEvent()
    {
        var trip = CreateTrip(Now.AddMinutes(120));
        var fixture = new Fixture(trip);

        await fixture.Coordinator.StartManualAsync(
            trip.Id,
            trip.DriverUserId,
            "DRIVER",
            null,
            Now,
            CancellationToken.None);
        var replay = await fixture.Coordinator.StartManualAsync(
            trip.Id,
            trip.DriverUserId,
            "DRIVER",
            null,
            Now,
            CancellationToken.None);

        replay.Status.Should().Be("BOARDING");
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Audits.Items.Should().ContainSingle();
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualBoardingAtT120_ThenExistingStart_ProducesOrderedLifecycle()
    {
        var trip = CreateTrip(Now.AddMinutes(120));
        var fixture = new Fixture(trip);
        await fixture.Coordinator.StartManualAsync(
            trip.Id,
            trip.DriverUserId,
            "DRIVER",
            null,
            Now,
            CancellationToken.None);
        var startHandler = new StartTripCommandHandler(
            fixture.Repository,
            fixture.Outbox,
            fixture.UnitOfWork,
            new FrozenClock(Now));

        var response = await startHandler.Handle(
            new StartTripCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        response.Status.Should().Be("IN_PROGRESS");
        response.ActualDepartureTime.Should().Be(Now);
        fixture.Outbox.Events.Select(item => item.EventType).Should().Equal(
            "trip.trip.boarding_started",
            "trip.trip.started");
        fixture.Audits.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task StartManual_RejectsWrongDriverAndMasksCrossTenantOperator()
    {
        var trip = CreateTrip(Now.AddMinutes(120));
        var fixture = new Fixture(trip);

        var wrongDriver = () => fixture.Coordinator.StartManualAsync(
            trip.Id,
            Guid.NewGuid(),
            "DRIVER",
            null,
            Now,
            CancellationToken.None);
        var crossTenant = () => fixture.Coordinator.StartManualAsync(
            trip.Id,
            Guid.NewGuid(),
            "OPERATOR_ADMIN",
            Guid.NewGuid(),
            Now,
            CancellationToken.None);

        await wrongDriver.Should().ThrowAsync<ForbiddenException>();
        (await crossTenant.Should().ThrowAsync<CodedNotFoundException>())
            .Which.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        trip.Status.Should().Be(TripStatus.SCHEDULED);
    }

    [Fact]
    public async Task TryStartAutomatic_AfterDriverStarted_DoesNotRegressStatus()
    {
        var trip = CreateTrip(Now.AddMinutes(20));
        trip.MarkBoarding(Now);
        trip.Start(Now);
        var fixture = new Fixture(trip);

        var changed = await fixture.Coordinator.TryStartAutomaticAsync(
            trip.Id,
            Now,
            CancellationToken.None);

        changed.Should().BeFalse();
        trip.Status.Should().Be(TripStatus.IN_PROGRESS);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(DateTimeOffset departure)
    {
        return VietRide.Trip.Domain.Entities.Trip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            departure,
            departure.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            5m);
    }

    private sealed class Fixture
    {
        public Fixture(VietRide.Trip.Domain.Entities.Trip trip)
        {
            Repository = new LifecycleTripRepository(trip);
            Audits = new RecordingAuditRepository();
            Outbox = new RecordingOutbox();
            UnitOfWork = new RecordingUnitOfWork();
            Coordinator = new TripBoardingTransitionCoordinator(
                Repository,
                Audits,
                Outbox,
                UnitOfWork,
                new FixedBoardingWindowProvider(TimeSpan.FromMinutes(180)));
        }

        public LifecycleTripRepository Repository { get; }
        public RecordingAuditRepository Audits { get; }
        public RecordingOutbox Outbox { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public TripBoardingTransitionCoordinator Coordinator { get; }
    }

    private sealed class LifecycleTripRepository : ITripRepository
    {
        private readonly VietRide.Trip.Domain.Entities.Trip trip;

        public LifecycleTripRepository(VietRide.Trip.Domain.Entities.Trip trip) => this.trip = trip;

        public Task<VietRide.Trip.Domain.Entities.Trip?> AcquireForLifecycleTransitionAsync(
            Guid tripId,
            CancellationToken cancellationToken) =>
            Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == tripId ? trip : null);

        public Task<VietRide.Trip.Domain.Entities.Trip?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VietRide.Trip.Domain.Entities.Trip?>(trip.Id == id ? trip : null);

        public Task<VietRide.Trip.Domain.Entities.Trip> AddAsync(
            VietRide.Trip.Domain.Entities.Trip entity,
            CancellationToken cancellationToken = default) => Task.FromResult(entity);

        public void Update(VietRide.Trip.Domain.Entities.Trip entity) { }

        public void Remove(VietRide.Trip.Domain.Entities.Trip entity) { }

        public IQueryable<VietRide.Trip.Domain.Entities.Trip> Query() => new[] { trip }.AsQueryable();

        public IQueryable<VietRide.Trip.Domain.Entities.Trip> QueryNoTracking() => Query();

        public Task<VietRide.Trip.Domain.Entities.Trip?> GetWithSeatsAsync(
            Guid tripId,
            CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class RecordingAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Items { get; } = [];

        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Items.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TripAuditLog>>(Items.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken) => operation();

        public Task BeginTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedBoardingWindowProvider(TimeSpan manualEarlyWindow)
        : ITripBoardingWindowProvider
    {
        public TimeSpan ManualEarlyWindow { get; } = manualEarlyWindow;
    }

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
