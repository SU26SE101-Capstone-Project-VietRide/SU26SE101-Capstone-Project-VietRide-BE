using FluentAssertions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class Day24NoShowOperationalSnapshotTests
{
    private static readonly DateTimeOffset Departure = new(2026, 7, 18, 1, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_StartedTripWithArrivedStop_MapsAuthoritativeNoShowAnchors()
    {
        var fixture = SnapshotFixture.Create(started: true, arrived: true);

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id),
            CancellationToken.None);

        result.Status.Should().Be(nameof(TripStatus.IN_PROGRESS));
        result.ActualDepartureTime.Should().Be(Departure);
        result.DestinationArrivedAt.Should().Be(Departure.AddHours(3));
        result.Stops.Should().ContainSingle().Which.Should().Match<InternalTripStopSnapshotDto>(stop =>
            stop.Status == nameof(TripStopStatus.ARRIVED) &&
            stop.Name == "Stop" &&
            stop.ActualArrivalTime == Departure.AddHours(1));
    }

    [Fact]
    public async Task Handle_ScheduledTripWithPendingStop_PreservesNullableOperationalAnchors()
    {
        var fixture = SnapshotFixture.Create(started: false, arrived: false);

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id),
            CancellationToken.None);

        result.ActualDepartureTime.Should().BeNull();
        result.DestinationArrivedAt.Should().BeNull();
        result.Stops.Should().ContainSingle().Which.Should().Match<InternalTripStopSnapshotDto>(stop =>
            stop.Status == nameof(TripStopStatus.PENDING) &&
            stop.ActualArrivalTime == null);
    }

    private sealed class SnapshotFixture
    {
        private SnapshotFixture(GetTripSnapshotHandler handler, TripEntity trip)
        {
            Handler = handler;
            Trip = trip;
        }

        public GetTripSnapshotHandler Handler { get; }
        public TripEntity Trip { get; }

        public static SnapshotFixture Create(bool started, bool arrived)
        {
            var operatorId = Guid.NewGuid();
            var origin = Station.Create("Origin", "origin", "HCM", "HCM");
            var destination = Station.Create("Destination", "destination", "Ha Noi", "Ha Noi");
            var route = RouteEntity.Create(
                operatorId,
                "Route",
                origin.Id,
                destination.Id,
                Money.FromRaw(250000),
                100m,
                180);
            var stop = Stop.Create(operatorId, "Stop", 10.5m, 106.5m);
            var trip = TripEntity.Create(
                operatorId,
                route.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                Departure.AddHours(-1),
                Departure.AddHours(4),
                TripSource.AUTO_FROM_SCHEDULE,
                Money.FromRaw(250000),
                1000m,
                0m);
            var tripStop = TripStop.Create(
                trip.Id,
                stop.Id,
                1,
                Departure.AddHours(1),
                true,
                true,
                50m);

            if (started)
            {
                trip.MarkBoarding(Departure.AddMinutes(-30));
                trip.Start(Departure);
                trip.MarkDestinationArrived(Departure.AddHours(3), Guid.NewGuid());
            }

            if (arrived)
            {
                tripStop.MarkArrived(Departure.AddHours(1));
            }

            var handler = new GetTripSnapshotHandler(
                new FakeTripRepository([trip]),
                new FakeRouteRepository([route]),
                new FakeRouteStopFareTemplateRepository([]),
                new FakeStationRepository([origin, destination]),
                new FakeStopRepository([stop]),
                new FakeTripSeatRepository([]),
                new FakeTripStopRepository([tripStop]),
                new FakeTripStopFareRepository([]));

            return new SnapshotFixture(handler, trip);
        }
    }

    private abstract class FakeRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly Func<TEntity, TId> idSelector;

        protected FakeRepository(List<TEntity> items, Func<TEntity, TId> idSelector)
        {
            Items = items;
            this.idSelector = idSelector;
        }

        protected List<TEntity> Items { get; }

        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<TId>.Default.Equals(idSelector(item), id)));

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken ct)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) => Items.Remove(entity);
        public IQueryable<TEntity> Query() => Items.AsQueryable();
        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class FakeTripRepository(List<TripEntity> items)
        : FakeRepository<TripEntity, Guid>(items, trip => trip.Id), ITripRepository
    {
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeRouteRepository(List<RouteEntity> items)
        : FakeRepository<RouteEntity, Guid>(items, route => route.Id), IRouteRepository
    {
        public Task<RouteEntity?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId));

        public Task<RouteEntity?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));

        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(
            Guid operatorId,
            string? search,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteEntity>>(Items.Where(route => route.OperatorId == operatorId).ToList());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));
    }

    private sealed class FakeStationRepository(List<Station> items)
        : FakeRepository<Station, Guid>(items, station => station.Id), IStationRepository
    {
        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string? q,
            string? city,
            string? province,
            Guid? locationId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Station>>(Items);
    }

    private sealed class FakeStopRepository(List<Stop> items)
        : FakeRepository<Stop, Guid>(items, stop => stop.Id), IStopRepository;

    private sealed class FakeTripSeatRepository(List<TripSeat> items)
        : FakeRepository<TripSeat, Guid>(items, seat => seat.Id), ITripSeatRepository;

    private sealed class FakeTripStopRepository(List<TripStop> items)
        : FakeRepository<TripStop, (Guid, Guid)>(items, stop => (stop.TripId, stop.StopId)), ITripStopRepository;

    private sealed class FakeTripStopFareRepository(List<TripStopFare> items)
        : FakeRepository<TripStopFare, (Guid, Guid)>(items, fare => (fare.TripId, fare.StopId)), ITripStopFareRepository;

    private sealed class FakeRouteStopFareTemplateRepository(List<RouteStopFareTemplate> items)
        : FakeRepository<RouteStopFareTemplate, Guid>(items, template => template.Id), IRouteStopFareTemplateRepository
    {
        public Task<bool> ExistsOverlappingAsync(
            Guid routeId,
            Guid stopId,
            DateTimeOffset effectiveFrom,
            DateTimeOffset? effectiveUntil,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(
            Guid routeId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>([]);
    }
}
