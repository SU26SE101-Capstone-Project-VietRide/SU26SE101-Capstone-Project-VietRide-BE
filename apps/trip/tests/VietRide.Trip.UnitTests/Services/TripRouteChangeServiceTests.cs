using FluentAssertions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Services;

public sealed class TripRouteChangeServiceTests
{
    private static readonly DateTimeOffset Departure =
        DateTimeOffset.Parse("2026-08-10T08:00:00+07:00");

    [Fact]
    public async Task ApplyAsync_ScheduledTrip_ReplacesNonArrivedStopsAndUpdatesPlannedArrival()
    {
        var fixture = CreateFixture(estimatedDurationMinutes: 95);
        var stalePending = TripStop.Create(
            fixture.Trip.Id,
            Guid.NewGuid(),
            1,
            Departure.AddMinutes(20),
            true,
            false,
            10m);
        var staleSkipped = TripStop.Create(
            fixture.Trip.Id,
            Guid.NewGuid(),
            2,
            Departure.AddMinutes(40),
            false,
            true,
            20m);
        staleSkipped.MarkSkipped();
        fixture.TripStops.Items.AddRange([stalePending, staleSkipped]);
        var first = AlternativeRouteStop.Create(
            fixture.AlternativeRoute.Id,
            Guid.NewGuid(),
            1,
            25,
            15m);
        var second = AlternativeRouteStop.Create(
            fixture.AlternativeRoute.Id,
            Guid.NewGuid(),
            2,
            70,
            42m);
        fixture.AlternativeRoutes.Stops.AddRange([second, first]);

        await fixture.Service.ApplyAsync(
            fixture.Trip,
            fixture.AlternativeRoute,
            [],
            Departure.AddMinutes(-5),
            CancellationToken.None);

        fixture.TripStops.Items.Should().HaveCount(2);
        fixture.TripStops.Items.OrderBy(stop => stop.OrderIndex).Should().SatisfyRespectively(
            stop => stop.Should().Match<TripStop>(item =>
                item.StopId == first.StopId
                && item.OrderIndex == 1
                && item.Status == TripStopStatus.PENDING
                && item.EstimatedArrivalTime == Departure.AddMinutes(25)
                && item.AllowPickup
                && item.AllowDropoff
                && item.DistanceFromOriginKm == 15m),
            stop => stop.Should().Match<TripStop>(item =>
                item.StopId == second.StopId
                && item.OrderIndex == 2
                && item.Status == TripStopStatus.PENDING
                && item.EstimatedArrivalTime == Departure.AddMinutes(70)
                && item.AllowPickup
                && item.AllowDropoff
                && item.DistanceFromOriginKm == 42m));
        fixture.Trip.AlternativeRouteId.Should().Be(fixture.AlternativeRoute.Id);
        fixture.Trip.EstimatedArrivalTime.Should().Be(Departure.AddMinutes(95));
        fixture.Trip.PlannedEtaSource.Should().Be(PlannedEtaSource.ROUTE_BASELINE);
    }

    [Fact]
    public async Task ApplyAsync_InProgressTrip_PreservesArrivedHistoryAndAppendsOnlyNewAlternativeStops()
    {
        var fixture = CreateFixture(estimatedDurationMinutes: 80);
        var actualDeparture = Departure.AddMinutes(17);
        fixture.Trip.MarkBoarding(Departure.AddMinutes(-10));
        fixture.Trip.Start(actualDeparture);
        var arrivedStopId = Guid.NewGuid();
        var arrived = TripStop.Create(
            fixture.Trip.Id,
            arrivedStopId,
            2,
            Departure.AddMinutes(20),
            true,
            true,
            12m);
        var arrivedAt = Departure.AddMinutes(24);
        arrived.MarkArrived(arrivedAt);
        var stalePending = TripStop.Create(
            fixture.Trip.Id,
            Guid.NewGuid(),
            4,
            Departure.AddMinutes(50),
            true,
            true,
            30m);
        fixture.TripStops.Items.AddRange([arrived, stalePending]);
        var duplicateArrived = AlternativeRouteStop.Create(
            fixture.AlternativeRoute.Id,
            arrivedStopId,
            1,
            10,
            7m);
        var next = AlternativeRouteStop.Create(
            fixture.AlternativeRoute.Id,
            Guid.NewGuid(),
            2,
            35,
            25m);
        var last = AlternativeRouteStop.Create(
            fixture.AlternativeRoute.Id,
            Guid.NewGuid(),
            3,
            60,
            45m);
        fixture.AlternativeRoutes.Stops.AddRange([last, duplicateArrived, next]);

        await fixture.Service.ApplyAsync(
            fixture.Trip,
            fixture.AlternativeRoute,
            [],
            actualDeparture.AddMinutes(5),
            CancellationToken.None);

        fixture.TripStops.Items.OrderBy(stop => stop.OrderIndex).Should().SatisfyRespectively(
            stop => stop.Should().BeSameAs(arrived),
            stop => stop.Should().Match<TripStop>(item =>
                item.StopId == next.StopId
                && item.OrderIndex == 3
                && item.Status == TripStopStatus.PENDING
                && item.EstimatedArrivalTime == actualDeparture.AddMinutes(35)),
            stop => stop.Should().Match<TripStop>(item =>
                item.StopId == last.StopId
                && item.OrderIndex == 4
                && item.Status == TripStopStatus.PENDING
                && item.EstimatedArrivalTime == actualDeparture.AddMinutes(60)));
        fixture.TripStops.Items.Should().ContainSingle(stop => stop.StopId == arrivedStopId);
        arrived.Status.Should().Be(TripStopStatus.ARRIVED);
        arrived.ActualArrivalTime.Should().Be(arrivedAt);
        arrived.OrderIndex.Should().Be(2);
        fixture.Trip.EstimatedArrivalTime.Should().Be(actualDeparture.AddMinutes(80));
    }

    private static Fixture CreateFixture(int estimatedDurationMinutes)
    {
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var trip = DomainTrip.Create(
            operatorId,
            routeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            Departure,
            Departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            null,
            0m);
        var alternativeRoute = AlternativeRoute.Create(
            routeId,
            "Flood bypass",
            Guid.NewGuid(),
            50m,
            estimatedDurationMinutes);
        var alternativeRoutes = new AlternativeRouteRepositoryStub(alternativeRoute);
        var tripStops = new TripStopRepositoryStub();
        var service = new TripRouteChangeService(alternativeRoutes, tripStops, new OutboxStub());
        return new Fixture(service, trip, alternativeRoute, alternativeRoutes, tripStops);
    }

    private sealed record Fixture(
        TripRouteChangeService Service,
        DomainTrip Trip,
        AlternativeRoute AlternativeRoute,
        AlternativeRouteRepositoryStub AlternativeRoutes,
        TripStopRepositoryStub TripStops);

    private sealed class AlternativeRouteRepositoryStub(AlternativeRoute route) : IAlternativeRouteRepository
    {
        public List<AlternativeRouteStop> Stops { get; } = [];

        public Task<AlternativeRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AlternativeRoute?>(id == route.Id ? route : null);

        public Task<AlternativeRoute?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid alternativeRouteId,
            CancellationToken cancellationToken) =>
            GetByIdAsync(alternativeRouteId, cancellationToken);

        public Task<bool> ExistsStopAsync(
            Guid alternativeRouteId,
            Guid stopId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Stops.Any(stop =>
                stop.AlternativeRouteId == alternativeRouteId && stop.StopId == stopId));

        public Task<bool> ExistsStopOrderIndexAsync(
            Guid alternativeRouteId,
            int orderIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(Stops.Any(stop =>
                stop.AlternativeRouteId == alternativeRouteId && stop.OrderIndex == orderIndex));

        public Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(
            Guid alternativeRouteId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlternativeRouteStop>>(Stops
                .Where(stop => stop.AlternativeRouteId == alternativeRouteId)
                .OrderBy(stop => stop.OrderIndex)
                .ThenBy(stop => stop.StopId)
                .ToArray());

        public Task<IReadOnlyList<TripRouteChangedCandidateStop>> ListCandidateStopsAsync(
            Guid alternativeRouteId,
            DateTimeOffset estimatedArrivalBase,
            CancellationToken cancellationToken)
        {
            var result = Stops
                .Where(stop => stop.AlternativeRouteId == alternativeRouteId)
                .OrderBy(stop => stop.OrderIndex)
                .Select(stop => new TripRouteChangedCandidateStop(
                    stop.StopId,
                    null,
                    $"Stop {stop.OrderIndex}",
                    stop.OrderIndex,
                    estimatedArrivalBase.AddMinutes(stop.EstimatedDurationFromOriginMinutes)))
                .ToList();
            result.Add(new TripRouteChangedCandidateStop(
                null,
                route.DestinationStationId,
                "Destination",
                result.Count + 1,
                estimatedArrivalBase.AddMinutes(route.EstimatedDurationMinutes ?? 0)));
            return Task.FromResult<IReadOnlyList<TripRouteChangedCandidateStop>>(result);
        }

        public Task ReplaceStopsAsync(
            Guid alternativeRouteId,
            IReadOnlyCollection<AlternativeRouteStop> replacementStops,
            CancellationToken cancellationToken)
        {
            Stops.RemoveAll(stop => stop.AlternativeRouteId == alternativeRouteId);
            Stops.AddRange(replacementStops);
            return Task.CompletedTask;
        }

        public Task<AlternativeRoute> AddAsync(AlternativeRoute entity, CancellationToken cancellationToken) =>
            Task.FromResult(entity);

        public void Update(AlternativeRoute entity) { }

        public void Remove(AlternativeRoute entity) { }

        public IQueryable<AlternativeRoute> Query() => new[] { route }.AsQueryable();

        public IQueryable<AlternativeRoute> QueryNoTracking() => Query();
    }

    private sealed class TripStopRepositoryStub : ITripStopRepository
    {
        public List<TripStop> Items { get; } = [];

        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(
            Guid tripId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TripStop>>(Items
                .Where(stop => stop.TripId == tripId)
                .OrderBy(stop => stop.OrderIndex)
                .ThenBy(stop => stop.StopId)
                .ToArray());

        public Task DeleteNonArrivedByTripAsync(Guid tripId, CancellationToken cancellationToken)
        {
            Items.RemoveAll(stop => stop.TripId == tripId && stop.Status != TripStopStatus.ARRIVED);
            return Task.CompletedTask;
        }

        public Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(stop =>
                stop.TripId == id.TripId && stop.StopId == id.StopId));

        public Task<TripStop> AddAsync(TripStop entity, CancellationToken cancellationToken)
        {
            Items.Should().NotContain(stop =>
                stop.TripId == entity.TripId
                && (stop.StopId == entity.StopId || stop.OrderIndex == entity.OrderIndex));
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TripStop entity) { }

        public void Remove(TripStop entity) => Items.Remove(entity);

        public IQueryable<TripStop> Query() => Items.AsQueryable();

        public IQueryable<TripStop> QueryNoTracking() => Query();
    }

    private sealed class OutboxStub : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
