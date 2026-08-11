using FluentAssertions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.GetShuttleRoadDistance;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class GetShuttleRoadDistanceTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(10_001)]
    public async Task Handle_ReturnsRawGoogleRoadDistance(int distanceMeters)
    {
        var fixture = Fixture.Create(new ShuttleDistanceOutcome.Success(distanceMeters));

        var result = await fixture.Handler.Handle(
            new GetShuttleRoadDistanceQuery(
                fixture.Trip.Id,
                ShuttleTrip.InboundDirection,
                10.71m,
                106.61m),
            CancellationToken.None);

        result.DistanceMeters.Should().Be(distanceMeters);
        fixture.DistanceClient.LastOrigin.Should().Be((10.70m, 106.60m));
        fixture.DistanceClient.LastDestination.Should().Be((10.71m, 106.61m));
    }

    [Fact]
    public async Task Handle_MapsGoogleFailureToFailClosed503()
    {
        var fixture = Fixture.Create(new ShuttleDistanceOutcome.Unavailable("timeout"));

        var action = () => fixture.Handler.Handle(
            new GetShuttleRoadDistanceQuery(
                fixture.Trip.Id,
                ShuttleTrip.OutboundDirection,
                10.81m,
                106.81m),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ShuttleDistanceUnavailableException>();
        exception.Which.ErrorCode.Should().Be("SHUTTLE_DISTANCE_UNAVAILABLE");
        exception.Which.StatusCode.Should().Be(503);
    }

    private sealed class Fixture
    {
        private Fixture(
            GetShuttleRoadDistanceHandler handler,
            TripEntity trip,
            RecordingDistanceClient distanceClient)
        {
            Handler = handler;
            Trip = trip;
            DistanceClient = distanceClient;
        }

        public GetShuttleRoadDistanceHandler Handler { get; }
        public TripEntity Trip { get; }
        public RecordingDistanceClient DistanceClient { get; }

        public static Fixture Create(ShuttleDistanceOutcome outcome)
        {
            var operatorId = Guid.NewGuid();
            var origin = Station.Create(
                "Origin",
                "origin",
                "HCM",
                "HCM",
                latitude: 10.70m,
                longitude: 106.60m,
                supportsShuttle: true);
            var destination = Station.Create(
                "Destination",
                "destination",
                "Can Tho",
                "Can Tho",
                latitude: 10.80m,
                longitude: 106.80m,
                supportsShuttle: true);
            var route = RouteEntity.Create(
                operatorId,
                "Route",
                origin.Id,
                destination.Id,
                Money.FromRaw(100_000),
                100m,
                120);
            var trip = TripEntity.Create(
                operatorId,
                route.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddHours(3),
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                100m,
                0m);
            var distanceClient = new RecordingDistanceClient(outcome);
            var handler = new GetShuttleRoadDistanceHandler(
                new FakeTripRepository([trip]),
                new FakeRouteRepository([route]),
                new FakeStationRepository([origin, destination]),
                distanceClient);

            return new Fixture(handler, trip, distanceClient);
        }
    }

    private sealed class RecordingDistanceClient(ShuttleDistanceOutcome outcome) : IShuttleDistanceClient
    {
        public (decimal Latitude, decimal Longitude) LastOrigin { get; private set; }
        public (decimal Latitude, decimal Longitude) LastDestination { get; private set; }

        public Task<ShuttleDistanceOutcome> CalculateAsync(
            decimal originLatitude,
            decimal originLongitude,
            decimal destinationLatitude,
            decimal destinationLongitude,
            CancellationToken cancellationToken)
        {
            LastOrigin = (originLatitude, originLongitude);
            LastDestination = (destinationLatitude, destinationLongitude);
            return Task.FromResult(outcome);
        }
    }

    private abstract class FakeRepository<TEntity>(List<TEntity> items)
        : IRepository<TEntity, Guid>
        where TEntity : BaseEntity<Guid>
    {
        public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(items.FirstOrDefault(item => item.Id == id));
        public Task<TEntity> AddAsync(TEntity entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) { }
        public IQueryable<TEntity> Query() => items.AsQueryable();
        public IQueryable<TEntity> QueryNoTracking() => items.AsQueryable();
    }

    private sealed class FakeTripRepository(List<TripEntity> items)
        : FakeRepository<TripEntity>(items), ITripRepository
    {
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeRouteRepository(List<RouteEntity> items)
        : FakeRepository<RouteEntity>(items), IRouteRepository
    {
        public Task<RouteEntity?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            GetByIdAsync(routeId, cancellationToken);
        public Task<RouteEntity?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            GetByIdAsync(routeId, cancellationToken);
        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteEntity>>([]);
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeStationRepository(List<Station> items)
        : FakeRepository<Station>(items), IStationRepository
    {
        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string? q,
            string? city,
            string? province,
            Guid? locationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Station>>([]);
    }
}
