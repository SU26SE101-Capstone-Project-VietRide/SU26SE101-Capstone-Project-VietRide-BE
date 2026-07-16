using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class ArriveTripStopCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 4, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AssignedCrew_MarksPendingStopAndEnqueuesCanonicalEvent(bool useAssistant)
    {
        var trip = CreateTrip(inProgress: true);
        var stop = CreateStop(trip.Id);
        var originalEta = stop.EstimatedArrivalTime;
        var actorUserId = useAssistant ? trip.AssistantUserId!.Value : trip.DriverUserId;
        var fixture = new Fixture(trip, stop);

        var response = await fixture.Handler.Handle(
            new ArriveTripStopCommand(trip.Id, stop.StopId, actorUserId),
            CancellationToken.None);

        response.Should().Be(new ArriveTripStopResponse(
            trip.Id,
            stop.StopId,
            "ARRIVED",
            Now));
        stop.Status.Should().Be(TripStopStatus.ARRIVED);
        stop.ActualArrivalTime.Should().Be(Now);
        stop.EstimatedArrivalTime.Should().Be(originalEta);
        fixture.LockOrder.Should().Equal("trip", "stop");
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events[0].EventType.Should().Be("trip.stop.arrived");

        using var document = JsonDocument.Parse(fixture.Outbox.Events[0].Payload);
        var payload = document.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        payload.GetProperty("occurredAt").GetDateTime().Should().Be(Now.UtcDateTime);
        payload.GetProperty("eventType").GetString().Should().Be("trip.stop.arrived");
        payload.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        payload.GetProperty("stopId").GetGuid().Should().Be(stop.StopId);
        payload.GetProperty("operatorId").GetGuid().Should().Be(trip.OperatorId);
        payload.GetProperty("actorUserId").GetGuid().Should().Be(actorUserId);
        payload.GetProperty("actualArrivalTime").GetDateTimeOffset().Should().Be(Now);
    }

    [Fact]
    public async Task Handle_MissingTrip_ThrowsNotFoundBeforeStopLock()
    {
        var fixture = new Fixture(null, null);

        var action = () => fixture.Handler.Handle(
            new ArriveTripStopCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        fixture.LockOrder.Should().Equal("trip");
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnassignedActor_ThrowsForbiddenBeforeStopLock()
    {
        var trip = CreateTrip(inProgress: true);
        var fixture = new Fixture(trip, CreateStop(trip.Id));

        var action = () => fixture.Handler.Handle(
            new ArriveTripStopCommand(trip.Id, fixture.Stop!.StopId, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        fixture.LockOrder.Should().Equal("trip");
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingStop_ThrowsExactNotFoundCode()
    {
        var trip = CreateTrip(inProgress: true);
        var fixture = new Fixture(trip, null);

        var action = () => fixture.Handler.Handle(
            new ArriveTripStopCommand(trip.Id, Guid.NewGuid(), trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_STOP_NOT_FOUND");
        fixture.LockOrder.Should().Equal("trip", "stop");
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_FinalizedStop_ThrowsConflictBeforeTripStatusCheck(bool skipped)
    {
        var trip = CreateTrip(inProgress: false);
        var stop = CreateStop(trip.Id);
        if (skipped)
        {
            stop.MarkSkipped();
        }
        else
        {
            stop.MarkArrived(Now.AddMinutes(-1));
        }

        var fixture = new Fixture(trip, stop);

        var action = () => fixture.Handler.Handle(
            new ArriveTripStopCommand(trip.Id, stop.StopId, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_STOP_ALREADY_FINALIZED");
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PendingStopOnNonProgressTrip_ThrowsExactValidationCode()
    {
        var trip = CreateTrip(inProgress: false);
        var stop = CreateStop(trip.Id);
        var fixture = new Fixture(trip, stop);

        var action = () => fixture.Handler.Handle(
            new ArriveTripStopCommand(trip.Id, stop.StopId, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_IN_PROGRESS");
        stop.Status.Should().Be(TripStopStatus.PENDING);
        stop.ActualArrivalTime.Should().BeNull();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    private static TripEntity CreateTrip(bool inProgress)
    {
        var trip = TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Now.AddHours(-2),
            Now.AddHours(2),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            5m);
        if (inProgress)
        {
            trip.MarkBoarding(Now.AddHours(-2));
            trip.Start(Now.AddHours(-2));
        }

        return trip;
    }

    private static TripStop CreateStop(Guid tripId)
        => TripStop.Create(
            tripId,
            Guid.NewGuid(),
            1,
            Now.AddMinutes(30),
            allowPickup: true,
            allowDropoff: true,
            distanceFromOriginKm: 50m);

    private sealed class Fixture
    {
        public Fixture(TripEntity? trip, TripStop? stop)
        {
            Stop = stop;
            LockOrder = [];
            Outbox = new RecordingOutbox();
            Handler = new ArriveTripStopCommandHandler(
                new FakeTripRepository(trip, LockOrder),
                new FakeTripStopRepository(stop, LockOrder),
                Outbox,
                new FrozenClock(Now));
        }

        public TripStop? Stop { get; }
        public List<string> LockOrder { get; }
        public RecordingOutbox Outbox { get; }
        public ArriveTripStopCommandHandler Handler { get; }
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        private readonly TripEntity? trip;
        private readonly List<string> lockOrder;

        public FakeTripRepository(TripEntity? trip, List<string> lockOrder)
        {
            this.trip = trip;
            this.lockOrder = lockOrder;
        }

        public Task<TripEntity?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
        {
            lockOrder.Add("trip");
            return Task.FromResult(trip?.Id == tripId ? trip : null);
        }

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(trip?.Id == id ? trip : null);

        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => Array.Empty<TripEntity>().AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeTripStopRepository : ITripStopRepository
    {
        private readonly TripStop? stop;
        private readonly List<string> lockOrder;

        public FakeTripStopRepository(TripStop? stop, List<string> lockOrder)
        {
            this.stop = stop;
            this.lockOrder = lockOrder;
        }

        public Task<TripStop?> GetForUpdateAsync(
            Guid tripId,
            Guid stopId,
            CancellationToken cancellationToken)
        {
            lockOrder.Add("stop");
            return Task.FromResult(
                stop?.TripId == tripId && stop.StopId == stopId ? stop : null);
        }

        public Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                stop?.TripId == id.TripId && stop.StopId == id.StopId ? stop : null);

        public Task<TripStop> AddAsync(TripStop entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripStop entity) => throw new NotSupportedException();
        public void Remove(TripStop entity) => throw new NotSupportedException();
        public IQueryable<TripStop> Query() => Array.Empty<TripStop>().AsQueryable();
        public IQueryable<TripStop> QueryNoTracking() => Query();
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

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
