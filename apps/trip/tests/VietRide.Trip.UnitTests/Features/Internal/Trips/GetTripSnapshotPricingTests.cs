using FluentAssertions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class GetTripSnapshotPricingTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = WindowStart.AddDays(1);

    [Theory]
    [MemberData(nameof(HalfOpenBoundaryCases))]
    public async Task ExplicitPricing_UsesHalfOpenTemplateWindow(
        DateTimeOffset pricingAt,
        long expectedFare)
    {
        var fixture = SnapshotFixture.Create();
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(150000),
            WindowStart,
            WindowEnd));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, pricingAt),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(expectedFare);
        fixture.Templates.LastPricingAt.Should().Be(pricingAt.ToUniversalTime());
    }

    public static TheoryData<DateTimeOffset, long> HalfOpenBoundaryCases => new()
    {
        { WindowStart.AddTicks(-1), 250000 },
        { WindowStart, 150000 },
        { WindowStart.AddHours(12), 150000 },
        { WindowEnd, 250000 },
    };

    [Fact]
    public async Task ExplicitPricing_OpenEndedTemplate_RemainsActive()
    {
        var fixture = SnapshotFixture.Create();
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(160000),
            WindowStart));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, WindowStart.AddYears(5)),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(160000);
    }

    [Fact]
    public async Task ExplicitPricing_LegacySnapshotAndActiveTemplate_TemplateWins()
    {
        var fixture = SnapshotFixture.Create();
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(150000),
            WindowStart));
        fixture.Fares.Items.Add(TripStopFare.Create(
            fixture.Trip.Id,
            fixture.Stop.Id,
            Money.FromRaw(90000),
            TripStopFareSource.TEMPLATE_SNAPSHOT));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, WindowStart.AddMinutes(1)),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(150000);
    }

    [Fact]
    public async Task ExplicitPricing_LegacySnapshotWithoutActiveTemplate_BaseFareWins()
    {
        var fixture = SnapshotFixture.Create();
        fixture.Fares.Items.Add(TripStopFare.Create(
            fixture.Trip.Id,
            fixture.Stop.Id,
            Money.FromRaw(90000),
            TripStopFareSource.TEMPLATE_SNAPSHOT));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, WindowStart.AddMinutes(1)),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(250000);
    }

    [Fact]
    public async Task ExplicitPricing_ManualOverride_WinsOverActiveTemplate()
    {
        var fixture = SnapshotFixture.Create();
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(150000),
            WindowStart));
        fixture.Fares.Items.Add(TripStopFare.Create(
            fixture.Trip.Id,
            fixture.Stop.Id,
            Money.FromRaw(175000),
            TripStopFareSource.MANUAL_OVERRIDE));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, WindowStart.AddMinutes(1)),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(175000);
    }

    [Theory]
    [InlineData(TripStopFareSource.TEMPLATE_SNAPSHOT)]
    [InlineData(TripStopFareSource.MANUAL_OVERRIDE)]
    public async Task OmittedPricing_PreservesPersistedFareAndDoesNotReadTemplates(TripStopFareSource source)
    {
        var fixture = SnapshotFixture.Create();
        fixture.Fares.Items.Add(TripStopFare.Create(
            fixture.Trip.Id,
            fixture.Stop.Id,
            Money.FromRaw(120000),
            source));
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(150000),
            WindowStart));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id),
            CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.FareFromThisStop.Should().Be(120000);
        fixture.Templates.ActiveLookupCount.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitPricing_AppliesSurchargeAfterResolvingTemplateFare()
    {
        var surcharge = new FakeFareSurchargeService(20);
        var fixture = SnapshotFixture.Create(surcharge);
        fixture.Templates.Items.Add(RouteStopFareTemplate.Create(
            fixture.Route.Id,
            fixture.Stop.Id,
            Money.FromRaw(150000),
            WindowStart));

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id, WindowStart.AddMinutes(1)),
            CancellationToken.None);

        result.OriginalBaseFare.Should().Be(250000);
        result.BaseFare.Should().Be(300000);
        result.SurchargePercent.Should().Be(20);
        result.SurchargeAmount.Should().Be(50000);
        result.Stops.Should().ContainSingle().Which.Should().Match<InternalTripStopSnapshotDto>(stop =>
            stop.OriginalFareFromThisStop == 150000
            && stop.FareFromThisStop == 180000
            && stop.SurchargePercent == 20
            && stop.SurchargeAmount == 30000);
        surcharge.ResolveCount.Should().Be(1);
    }

    [Fact]
    public async Task OmittedPricing_DoesNotResolveOrApplySurcharge()
    {
        var surcharge = new FakeFareSurchargeService(20);
        var fixture = SnapshotFixture.Create(surcharge);

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id),
            CancellationToken.None);

        result.BaseFare.Should().Be(250000);
        result.SurchargePercent.Should().Be(0);
        surcharge.ResolveCount.Should().Be(0);
    }

    [Fact]
    public async Task Snapshot_ExposesNullableRouteTotalDistanceForPerBookingDisruptionRefunds()
    {
        var fixture = SnapshotFixture.Create();

        var result = await fixture.Handler.Handle(
            new GetTripSnapshotQuery(fixture.Trip.Id),
            CancellationToken.None);

        result.TotalDistanceKm.Should().Be(100d);
        result.Stops.Should().ContainSingle().Which.DistanceFromOriginKm.Should().Be(50d);
    }

    private sealed class SnapshotFixture
    {
        private SnapshotFixture(
            GetTripSnapshotHandler handler,
            TripEntity trip,
            RouteEntity route,
            Stop stop,
            FakeRouteStopFareTemplateRepository templates,
            FakeTripStopFareRepository fares)
        {
            Handler = handler;
            Trip = trip;
            Route = route;
            Stop = stop;
            Templates = templates;
            Fares = fares;
        }

        public GetTripSnapshotHandler Handler { get; }
        public TripEntity Trip { get; }
        public RouteEntity Route { get; }
        public Stop Stop { get; }
        public FakeRouteStopFareTemplateRepository Templates { get; }
        public FakeTripStopFareRepository Fares { get; }

        public static SnapshotFixture Create(IFareSurchargeService? fareSurchargeService = null)
        {
            var operatorId = Guid.NewGuid();
            var origin = Station.Create("Origin", "origin", "HCM", "HCM");
            var destination = Station.Create("Destination", "destination", "Can Tho", "Can Tho");
            var route = RouteEntity.Create(
                operatorId,
                "Route",
                origin.Id,
                destination.Id,
                Money.FromRaw(250000),
                100m,
                120);
            var stop = Stop.Create(operatorId, "Stop", 10.5m, 106.5m);
            var departure = WindowStart.AddDays(2);
            var trip = TripEntity.Create(
                operatorId,
                route.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                departure,
                departure.AddHours(2),
                TripSource.AUTO_FROM_SCHEDULE,
                Money.FromRaw(250000),
                1000m,
                0m);
            var tripStop = TripStop.Create(trip.Id, stop.Id, 1, departure.AddHours(1), true, true, 50m);
            var templates = new FakeRouteStopFareTemplateRepository([]);
            var fares = new FakeTripStopFareRepository([]);
            var handler = new GetTripSnapshotHandler(
                new FakeTripRepository([trip]),
                new FakeRouteRepository([route]),
                templates,
                new FakeStationRepository([origin, destination]),
                new FakeStopRepository([stop]),
                new FakeTripSeatRepository([]),
                new FakeTripStopRepository([tripStop]),
                fares,
                fareSurchargeService);

            return new SnapshotFixture(handler, trip, route, stop, templates, fares);
        }
    }

    private sealed class FakeFareSurchargeService(int percent) : IFareSurchargeService
    {
        private readonly FareSurchargeRule rule = new(Guid.NewGuid(), "Holiday", percent);

        public int ResolveCount { get; private set; }

        public Task<FareSurchargeRule?> ResolveAsync(
            Guid operatorId,
            DateTimeOffset departureDateTime,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return Task.FromResult<FareSurchargeRule?>(rule);
        }

        public FareSurchargeAdjustment Apply(long originalFare, FareSurchargeRule? surchargeRule)
        {
            if (surchargeRule is null)
                return new(originalFare, 0, 0, originalFare, null, null);

            var effectiveFare = checked((long)decimal.Round(
                originalFare * (100m + surchargeRule.Percent) / 100m,
                0,
                MidpointRounding.AwayFromZero));
            return new(
                originalFare,
                surchargeRule.Percent,
                effectiveFare - originalFare,
                effectiveFare,
                surchargeRule.PeriodId,
                surchargeRule.PeriodName);
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

        public List<TEntity> Items { get; }
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
        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteEntity>>(Items.Where(route => route.OperatorId == operatorId).ToList());
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
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
        public int ActiveLookupCount { get; private set; }
        public DateTimeOffset? LastPricingAt { get; private set; }

        public Task<bool> ExistsOverlappingAsync(
            Guid routeId,
            Guid stopId,
            DateTimeOffset effectiveFrom,
            DateTimeOffset? effectiveUntil,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(
            Guid routeId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(Items.Where(template => template.RouteId == routeId).ToList());

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListActiveByRouteAsync(
            Guid routeId,
            DateTimeOffset pricingAt,
            CancellationToken cancellationToken)
        {
            ActiveLookupCount++;
            LastPricingAt = pricingAt;
            return Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(Items
                .Where(template => template.RouteId == routeId
                    && template.EffectiveFrom <= pricingAt
                    && (!template.EffectiveUntil.HasValue || pricingAt < template.EffectiveUntil.Value))
                .OrderBy(template => template.StopId)
                .ThenByDescending(template => template.EffectiveFrom)
                .ThenBy(template => template.Id)
                .ToList());
        }
    }
}
