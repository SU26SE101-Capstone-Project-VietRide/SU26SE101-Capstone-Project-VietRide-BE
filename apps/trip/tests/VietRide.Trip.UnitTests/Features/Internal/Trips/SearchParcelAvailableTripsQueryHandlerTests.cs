using System.Collections;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Query;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class SearchParcelAvailableTripsQueryHandlerTests
{
    private static readonly DateOnly DepartureDate = new(2026, 7, 27);
    private static readonly DateTimeOffset Departure = new(2026, 7, 27, 8, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public async Task Handle_ReturnsStatusEtaStations_AndRequiresBothWeightAndVolumeCapacity()
    {
        var fixture = Fixture.Create();
        var enough = fixture.CreateTrip(Departure, 100m, 10m);
        var insufficientVolume = fixture.CreateTrip(Departure.AddHours(1), 100m, 0.0005m);
        fixture.Trips.Items.AddRange([enough, insufficientVolume]);

        var result = await fixture.Handler.Handle(fixture.Query(weightKg: 1m, volumeM3: 0.001m), CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.TripId.Should().Be(enough.Id);
        item.Status.Should().Be("SCHEDULED");
        item.EstimatedArrivalTime.Should().Be(enough.EstimatedArrivalTime);
        item.OriginStation.Should().Be(new ParcelTripStationDto(fixture.Origin.Id, fixture.Origin.Name));
        item.DestinationStation.Should().Be(new ParcelTripStationDto(fixture.Destination.Id, fixture.Destination.Name));
        item.DropoffPoints.Should().ContainSingle().Which.Should().Be(new ParcelTripDropoffPointDto(
            "STATION",
            fixture.Destination.Id,
            null,
            fixture.Destination.Name,
            1,
            enough.EstimatedArrivalTime));
        item.AvailableCargoWeightKg.Should().Be(100m);
        item.AvailableCargoVolumeM3.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_ExcludesBoardingTripsBeforeCountAndPagination()
    {
        var fixture = Fixture.Create();
        var scheduled = fixture.CreateTrip(Departure, 100m, 10m);
        var boarding = fixture.CreateTrip(Departure.AddHours(1), 100m, 10m);
        boarding.MarkBoarding(Departure.AddMinutes(-30));
        fixture.Trips.Items.AddRange([scheduled, boarding]);

        var result = await fixture.Handler.Handle(fixture.Query(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.TripId.Should().Be(scheduled.Id);
        result.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExcludesTripWithoutAssignedAssistantBeforeCountAndPagination()
    {
        var fixture = Fixture.Create();
        var staffed = fixture.CreateTrip(Departure, 100m, 10m);
        var noAssistant = fixture.CreateTrip(Departure.AddHours(1), 100m, 10m, hasAssistant: false);
        fixture.Trips.Items.AddRange([staffed, noAssistant]);

        var result = await fixture.Handler.Handle(fixture.Query(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.TripId.Should().Be(staffed.Id);
        result.TotalItems.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_ExcludesRoute_WhenEitherStationIsInactiveOrDeleted(
        bool destination,
        bool softDelete)
    {
        var fixture = Fixture.Create();
        var station = destination ? fixture.Destination : fixture.Origin;
        if (softDelete)
            station.SoftDelete(DateTimeOffset.UtcNow);
        else
            station.Deactivate();
        fixture.Trips.Items.Add(fixture.CreateTrip(Departure, 100m, 10m));

        var result = await fixture.Handler.Handle(fixture.Query(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_PreservesDepartureOrderingAndPagination()
    {
        var fixture = Fixture.Create();
        var later = fixture.CreateTrip(Departure.AddHours(2), 100m, 10m);
        var earlier = fixture.CreateTrip(Departure, 100m, 10m);
        fixture.Trips.Items.AddRange([later, earlier]);

        var firstPage = await fixture.Handler.Handle(fixture.Query(page: 1, pageSize: 1), CancellationToken.None);
        var secondPage = await fixture.Handler.Handle(fixture.Query(page: 2, pageSize: 1), CancellationToken.None);

        firstPage.Items.Should().ContainSingle().Which.TripId.Should().Be(earlier.Id);
        secondPage.Items.Should().ContainSingle().Which.TripId.Should().Be(later.Id);
        firstPage.TotalItems.Should().Be(2);
        secondPage.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Handle_AppliesEligibleRoutesBeforeCountAndPagination()
    {
        var fixture = Fixture.Create();
        fixture.Trips.Items.Add(fixture.CreateTrip(Departure, 100m, 10m));

        var excluded = await fixture.Handler.Handle(
            fixture.Query(eligibleRouteIds: [Guid.NewGuid()]),
            CancellationToken.None);
        var included = await fixture.Handler.Handle(
            fixture.Query(eligibleRouteIds: [fixture.Route.Id, fixture.Route.Id]),
            CancellationToken.None);

        excluded.Items.Should().BeEmpty();
        excluded.TotalItems.Should().Be(0);
        included.Items.Should().ContainSingle();
        included.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExactStopMode_ReturnsOnlyTripsWithActiveDropoffStop_BeforePagination()
    {
        var fixture = Fixture.Create(withLocations: true);
        var withoutStop = fixture.CreateTrip(Departure, 100m, 10m);
        var withStop = fixture.CreateTrip(Departure.AddHours(1), 100m, 10m);
        fixture.Trips.Items.AddRange([withoutStop, withStop]);
        var stop = fixture.AddStop(withStop, allowDropoff: true);

        var result = await fixture.Handler.Handle(
            fixture.Query(useDefaultStation: false, dropoffStopId: stop.Id, pageSize: 1),
            CancellationToken.None);

        result.TotalItems.Should().Be(1);
        var item = result.Items.Should().ContainSingle().Which;
        item.TripId.Should().Be(withStop.Id);
        item.DropoffPoints.Should().ContainSingle().Which.Should().Be(new ParcelTripDropoffPointDto(
            "STOP",
            null,
            stop.Id,
            stop.Name,
            1,
            withStop.DepartureDateTime.AddHours(2)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Handle_ExactStopMode_ExcludesInactiveOrNonDropoffStop(bool deactivate, bool allowDropoff)
    {
        var fixture = Fixture.Create(withLocations: true);
        var trip = fixture.CreateTrip(Departure, 100m, 10m);
        fixture.Trips.Items.Add(trip);
        var stop = fixture.AddStop(trip, allowDropoff);
        if (deactivate)
            stop.Deactivate();

        var result = await fixture.Handler.Handle(
            fixture.Query(useDefaultStation: false, dropoffStopId: stop.Id),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_LocationMode_ReturnsMatchingStopsAndDestinationTerminalInOrder()
    {
        var fixture = Fixture.Create(withLocations: true);
        var trip = fixture.CreateTrip(Departure, 100m, 10m);
        fixture.Trips.Items.Add(trip);
        var stop = fixture.AddStop(trip, allowDropoff: true);

        var result = await fixture.Handler.Handle(
            fixture.Query(
                useDefaultStation: false,
                destinationProvinceCode: fixture.Province!.Code),
            CancellationToken.None);

        var points = result.Items.Should().ContainSingle().Which.DropoffPoints;
        points.Should().HaveCount(2);
        points[0].Type.Should().Be("STOP");
        points[0].StopId.Should().Be(stop.Id);
        points[0].StationId.Should().BeNull();
        points[1].Type.Should().Be("STATION");
        points[1].StationId.Should().Be(fixture.Destination.Id);
        points[1].StopId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RejectsMissingOrCombinedDestinationModes()
    {
        var fixture = Fixture.Create();

        var missing = () => fixture.Handler.Handle(
            fixture.Query(useDefaultStation: false),
            CancellationToken.None);
        var combined = () => fixture.Handler.Handle(
            fixture.Query(dropoffStopId: Guid.NewGuid()),
            CancellationToken.None);

        (await missing.Should().ThrowAsync<CodedValidationException>()).Which.ErrorCode
            .Should().Be("VALIDATION_ERROR");
        (await combined.Should().ThrowAsync<CodedValidationException>()).Which.ErrorCode
            .Should().Be("VALIDATION_ERROR");
    }

    private sealed class Fixture
    {
        private Fixture(
            Guid operatorId,
            Station origin,
            Station destination,
            RouteEntity route,
            FakeTripRepository trips,
            FakeTripStopRepository tripStops,
            FakeStopRepository stops,
            Location? province,
            Location? destinationLocation,
            SearchParcelAvailableTripsQueryHandler handler)
        {
            OperatorId = operatorId;
            Origin = origin;
            Destination = destination;
            Route = route;
            Trips = trips;
            TripStops = tripStops;
            Stops = stops;
            Province = province;
            DestinationLocation = destinationLocation;
            Handler = handler;
        }

        public Guid OperatorId { get; }
        public Station Origin { get; }
        public Station Destination { get; }
        public RouteEntity Route { get; }
        public FakeTripRepository Trips { get; }
        public FakeTripStopRepository TripStops { get; }
        public FakeStopRepository Stops { get; }
        public Location? Province { get; }
        public Location? DestinationLocation { get; }
        public SearchParcelAvailableTripsQueryHandler Handler { get; }

        public static Fixture Create(bool withLocations = false)
        {
            var operatorId = Guid.NewGuid();
            var province = withLocations
                ? Location.Create("79", "Ho Chi Minh City", Location.MunicipalityType, 1)
                : null;
            var destinationLocation = withLocations
                ? Location.Create("760", "District 1", Location.WardType, province!.Id, 1)
                : null;
            var origin = Station.Create("Origin Station", $"origin-{Guid.NewGuid():N}", "HCM", "HCM");
            var destination = Station.Create(
                "Destination Station",
                $"destination-{Guid.NewGuid():N}",
                "Da Nang",
                "Da Nang",
                locationId: destinationLocation?.Id);
            var route = RouteEntity.Create(
                operatorId,
                "HCM - Da Nang",
                origin.Id,
                destination.Id,
                Money.FromRaw(150_000),
                900m,
                720);
            var trips = new FakeTripRepository([]);
            var tripStops = new FakeTripStopRepository([]);
            var stops = new FakeStopRepository([]);
            var locations = new FakeLocationRepository(
                new[] { province, destinationLocation }.Where(location => location is not null).Cast<Location>().ToList());
            var handler = new SearchParcelAvailableTripsQueryHandler(
                new FakeRouteRepository([route]),
                trips,
                new FakeStationRepository([origin, destination]),
                new FakeIdentityInternalClient(operatorId, "VietRide Express"),
                tripStops,
                stops,
                locations);
            return new Fixture(
                operatorId,
                origin,
                destination,
                route,
                trips,
                tripStops,
                stops,
                province,
                destinationLocation,
                handler);
        }

        public TripEntity CreateTrip(
            DateTimeOffset departure,
            decimal maxWeightKg,
            decimal maxVolumeM3,
            bool hasAssistant = true)
            => TripEntity.Create(
                OperatorId,
                Route.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                hasAssistant ? Guid.NewGuid() : null,
                null,
                departure,
                departure.AddHours(12),
                TripSource.AUTO_FROM_SCHEDULE,
                Money.FromRaw(150_000),
                maxWeightKg,
                maxVolumeM3,
                0m);

        public SearchParcelAvailableTripsQuery Query(
            decimal weightKg = 1m,
            decimal volumeM3 = 0.001m,
            int page = 1,
            int pageSize = 20,
            IReadOnlyCollection<Guid>? eligibleRouteIds = null,
            bool useDefaultStation = true,
            Guid? destinationStationId = null,
            Guid? dropoffStopId = null,
            string? destinationProvinceCode = null,
            string? destinationLocationCode = null)
            => new(
                Origin.Id,
                useDefaultStation ? Destination.Id : destinationStationId,
                DepartureDate,
                weightKg,
                volumeM3,
                "MEDIUM",
                page,
                pageSize,
                eligibleRouteIds,
                dropoffStopId,
                destinationProvinceCode,
                destinationLocationCode);

        public Stop AddStop(TripEntity trip, bool allowDropoff)
        {
            var stop = Stop.Create(
                OperatorId,
                $"Dropoff {Stops.Items.Count + 1}",
                10.1m,
                106.1m,
                locationId: DestinationLocation?.Id);
            Stops.Items.Add(stop);
            TripStops.Items.Add(TripStop.Create(
                trip.Id,
                stop.Id,
                1,
                trip.DepartureDateTime.AddHours(2),
                allowPickup: true,
                allowDropoff,
                10m));
            return stop;
        }
    }

    private abstract class FakeRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly Func<TEntity, TId> _idSelector;

        protected FakeRepository(List<TEntity> items, Func<TEntity, TId> idSelector)
        {
            Items = items;
            _idSelector = idSelector;
        }

        public List<TEntity> Items { get; }
        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<TId>.Default.Equals(_idSelector(item), id)));
        public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) => Items.Remove(entity);
        public IQueryable<TEntity> Query() => new TestAsyncEnumerable<TEntity>(Items);
        public IQueryable<TEntity> QueryNoTracking() => new TestAsyncEnumerable<TEntity>(Items);
    }

    private sealed class FakeRouteRepository : FakeRepository<RouteEntity, Guid>, IRouteRepository
    {
        public FakeRouteRepository(List<RouteEntity> items)
            : base(items, route => route.Id)
        {
        }

        public Task<RouteEntity?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId));
        public Task<RouteEntity?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));
        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteEntity>>(Items.Where(route => route.OperatorId == operatorId).ToList());
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));
    }

    private sealed class FakeTripRepository : FakeRepository<TripEntity, Guid>, ITripRepository
    {
        public FakeTripRepository(List<TripEntity> items)
            : base(items, trip => trip.Id)
        {
        }

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeStationRepository : FakeRepository<Station, Guid>, IStationRepository
    {
        public FakeStationRepository(List<Station> items)
            : base(items, station => station.Id)
        {
        }

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string? q,
            string? city,
            string? province,
            Guid? locationId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Station>>(Items);
    }

    private sealed class FakeTripStopRepository : FakeRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public FakeTripStopRepository(List<TripStop> items)
            : base(items, tripStop => (tripStop.TripId, tripStop.StopId))
        {
        }
    }

    private sealed class FakeStopRepository : FakeRepository<Stop, Guid>, IStopRepository
    {
        public FakeStopRepository(List<Stop> items)
            : base(items, stop => stop.Id)
        {
        }
    }

    private sealed class FakeLocationRepository : FakeRepository<Location, Guid>, ILocationRepository
    {
        public FakeLocationRepository(List<Location> items)
            : base(items, location => location.Id)
        {
        }

        public Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(location => location.Id == id && location.IsActive));

        public Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(location => location.Code == code && location.IsActive));

        public Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(location => location.Code == code && location.Id != exceptId));

        public Task<PagedResult<Location>> ListAsync(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<Location>.Create(Items, page, pageSize, Items.Count));
    }

    private sealed class FakeIdentityInternalClient : IIdentityInternalClient
    {
        private readonly Guid _operatorId;
        private readonly string _operatorName;

        public FakeIdentityInternalClient(Guid operatorId, string operatorName)
        {
            _operatorId = operatorId;
            _operatorName = operatorName;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IdentityUserLookupResult.ValidationFailure("Not used."));

        public Task<IdentityOperatorLookupResult> GetOperatorAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id == _operatorId
                ? IdentityOperatorLookupResult.Success(id, _operatorName)
                : IdentityOperatorLookupResult.ValidationFailure("Not found."));
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
        public TestAsyncEnumerable(Expression expression) : base(expression) { }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public T Current => _inner.Current;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner)
        {
            _inner = inner;
        }

        public IQueryable CreateQuery(Expression expression) =>
            (IQueryable)Activator.CreateInstance(
                typeof(TestAsyncEnumerable<>).MakeGenericType(expression.Type.GetGenericArguments()[0]),
                expression)!;

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(resultType)
                .Invoke(_inner, [expression]);
            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [executionResult])!;
        }
    }
}
