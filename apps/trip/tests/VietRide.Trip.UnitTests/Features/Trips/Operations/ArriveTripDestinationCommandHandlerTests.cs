using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class ArriveTripDestinationCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AssignedCrewOnExpressTrip_RecordsAnchorAndCanonicalEvent(bool useAssistant)
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);
        var actorUserId = useAssistant ? trip.AssistantUserId!.Value : trip.DriverUserId;
        var fixture = new Fixture(trip, route);

        var response = await fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, actorUserId),
            CancellationToken.None);

        response.Should().Be(new ArriveTripDestinationResponse(
            trip.Id,
            route.DestinationStationId,
            "ARRIVED",
            Now));
        trip.DestinationArrivedAt.Should().Be(Now);
        trip.DestinationArrivedByUserId.Should().Be(actorUserId);
        trip.CompletedAt.Should().BeNull();
        trip.Status.Should().Be(TripStatus.IN_PROGRESS);
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events[0].EventType.Should().Be("trip.destination.arrived");

        using var document = JsonDocument.Parse(fixture.Outbox.Events[0].Payload);
        var payload = document.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        payload.GetProperty("occurredAt").GetDateTime().Should().Be(Now.UtcDateTime);
        payload.GetProperty("eventType").GetString().Should().Be("trip.destination.arrived");
        payload.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        payload.GetProperty("destinationStationId").GetGuid().Should().Be(route.DestinationStationId);
        payload.GetProperty("operatorId").GetGuid().Should().Be(trip.OperatorId);
        payload.GetProperty("actorUserId").GetGuid().Should().Be(actorUserId);
        payload.GetProperty("actualArrivalTime").GetDateTimeOffset().Should().Be(Now);
    }

    [Fact]
    public async Task Handle_MissingTrip_ThrowsExactNotFoundCode()
    {
        var fixture = new Fixture(null, null);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        fixture.RouteLookupCount.Should().Be(0);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnassignedActor_ThrowsForbiddenBeforeRouteLookup()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);
        var fixture = new Fixture(trip, route);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        fixture.RouteLookupCount.Should().Be(0);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AlreadyArrived_ThrowsConflictBeforeTripStatusCheck()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);
        trip.MarkDestinationArrived(Now.AddMinutes(-1), trip.DriverUserId);
        trip.CompleteAutomatically(Now);
        var fixture = new Fixture(trip, route);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_DESTINATION_ALREADY_ARRIVED");
        fixture.RouteLookupCount.Should().Be(0);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonProgressTrip_ThrowsExactValidationCode()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: false);
        var fixture = new Fixture(trip, route);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_IN_PROGRESS");
        trip.DestinationArrivedAt.Should().BeNull();
        fixture.RouteLookupCount.Should().Be(0);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingRouteSnapshot_ThrowsNotFoundWithoutAnchor()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);
        var fixture = new Fixture(trip, null);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        trip.DestinationArrivedAt.Should().BeNull();
        fixture.RouteLookupCount.Should().Be(1);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PriorStopNotDeparted_RejectsDestinationArrival()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);
        var prior = TripStop.Create(
            trip.Id,
            Guid.NewGuid(),
            1,
            Now.AddMinutes(-30),
            allowPickup: true,
            allowDropoff: true,
            distanceFromOriginKm: 10m);
        prior.MarkArrived(Now.AddMinutes(-10));
        var fixture = new Fixture(trip, route, [prior]);

        var action = () => fixture.Handler.Handle(
            new ArriveTripDestinationCommand(trip.Id, trip.DriverUserId),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_STOP_SEQUENCE_VIOLATION");
        exception.Errors.Should().Contain(error =>
            error.Field == "target" && error.Message == "DESTINATION");
        trip.DestinationArrivedAt.Should().BeNull();
        fixture.RouteLookupCount.Should().Be(0);
    }

    [Fact]
    public void CompleteAutomatically_DoesNotSynthesizeDestinationAnchor()
    {
        var route = CreateRoute();
        var trip = CreateTrip(route.Id, inProgress: true);

        trip.CompleteAutomatically(Now);

        trip.Status.Should().Be(TripStatus.COMPLETED);
        trip.CompletedAt.Should().Be(Now);
        trip.DestinationArrivedAt.Should().BeNull();
        trip.DestinationArrivedByUserId.Should().BeNull();
    }

    private static RouteEntity CreateRoute()
        => RouteEntity.Create(
            Guid.NewGuid(),
            "Express route",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            300m,
            240);

    private static TripEntity CreateTrip(Guid routeId, bool inProgress)
    {
        var trip = TripEntity.Create(
            Guid.NewGuid(),
            routeId,
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

    private sealed class Fixture
    {
        private readonly CountingRouteRepository routeRepository;

        public Fixture(
            TripEntity? trip,
            RouteEntity? route,
            IReadOnlyList<TripStop>? stops = null)
        {
            routeRepository = new CountingRouteRepository(route);
            Outbox = new RecordingOutbox();
            Handler = new ArriveTripDestinationCommandHandler(
                new FakeTripRepository(trip),
                new FakeTripStopRepository(stops ?? []),
                routeRepository,
                Outbox,
                new FrozenClock(Now));
        }

        public int RouteLookupCount => routeRepository.GetByIdCount;
        public RecordingOutbox Outbox { get; }
        public ArriveTripDestinationCommandHandler Handler { get; }
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        private readonly TripEntity? trip;

        public FakeTripRepository(TripEntity? trip)
        {
            this.trip = trip;
        }

        public Task<TripEntity?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult(trip?.Id == tripId ? trip : null);

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
        private readonly IReadOnlyList<TripStop> stops;

        public FakeTripStopRepository(IReadOnlyList<TripStop> stops)
        {
            this.stops = stops;
        }

        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(
            Guid tripId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TripStop>>(
                stops.Where(stop => stop.TripId == tripId).ToArray());

        public Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TripStop?>(null);

        public Task<TripStop> AddAsync(TripStop entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripStop entity) => throw new NotSupportedException();
        public void Remove(TripStop entity) => throw new NotSupportedException();
        public IQueryable<TripStop> Query() => Array.Empty<TripStop>().AsQueryable();
        public IQueryable<TripStop> QueryNoTracking() => Query();
    }

    private sealed class CountingRouteRepository : IRouteRepository
    {
        private readonly RouteEntity? route;

        public CountingRouteRepository(RouteEntity? route)
        {
            this.route = route;
        }

        public int GetByIdCount { get; private set; }

        public Task<RouteEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetByIdCount++;
            return Task.FromResult(route?.Id == id ? route : null);
        }

        public Task<RouteEntity> AddAsync(RouteEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(RouteEntity entity) => throw new NotSupportedException();
        public void Remove(RouteEntity entity) => throw new NotSupportedException();
        public IQueryable<RouteEntity> Query() => Array.Empty<RouteEntity>().AsQueryable();
        public IQueryable<RouteEntity> QueryNoTracking() => Query();

        public Task<RouteEntity?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RouteEntity?> GetOwnedActiveByIdAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(
            Guid operatorId,
            string? search,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ExistsActiveOwnedByOperatorAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
