using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.SearchTrips;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class TripHandlerProjectionTests
{
    [Fact]
    public async Task CancelPreview_AggregatesConfirmedBookingAndParcelRefunds()
    {
        var operatorId = Guid.NewGuid();
        var trip = CreateTrip(operatorId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1));
        var confirmedBookingId = Guid.NewGuid();
        var pendingBookingId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var handler = new CancelTripPreviewQueryHandler(
            new InMemoryTripRepository([trip]),
            new FakeBookingImpactClient(new TripBookingImpactProjection(
                trip.Id,
                2,
                [
                    new TripBookingImpactProjection.ActiveBooking(confirmedBookingId, "CONFIRMED", ["A01"], 250_000),
                    new TripBookingImpactProjection.ActiveBooking(pendingBookingId, "PENDING_PAYMENT", ["A02"], 400_000),
                ])),
            new FakeParcelImpactClient(new TripParcelCancellationImpactProjection(
                trip.Id,
                [new TripParcelCancellationImpactProjection.AffectedParcel(parcelId, "PENDING", 75_000)])));

        var result = await handler.Handle(
            new CancelTripPreviewQuery(trip.Id, operatorId),
            CancellationToken.None);

        result.AffectedBookingIds.Should().BeEquivalentTo([confirmedBookingId, pendingBookingId]);
        result.RefundTotalBooking.Should().Be(250_000);
        result.AffectedParcelIds.Should().Equal(parcelId);
        result.RefundTotalParcel.Should().Be(75_000);
        result.GrandTotal.Should().Be(325_000);
    }

    [Fact]
    public async Task Search_IncludesScheduledAndBoardingTrips()
    {
        var fixture = SearchFixture.Create();
        var scheduled = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        var boarding = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T09:00:00+07:00"));
        boarding.MarkBoarding(DateTimeOffset.Parse("2026-05-18T08:45:00+07:00"));
        fixture.Trips.AddRange([scheduled, boarding]);
        fixture.Seats.AddRange([
            TripSeat.Create(scheduled.Id, "A01"),
            TripSeat.Create(boarding.Id, "B01")]);

        var result = await fixture.Handler.Handle(fixture.Query, CancellationToken.None);

        result.Items.Select(item => item.TripId).Should().Equal(scheduled.Id, boarding.Id);
    }

    [Fact]
    public async Task Search_UsesIdentityOperatorName()
    {
        var fixture = SearchFixture.Create(operatorName: "Saigon Express Limousine");
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(fixture.Query, CancellationToken.None);

        result.Items.Should().ContainSingle()
            .Which.OperatorName.Should().Be("Saigon Express Limousine");
    }

    [Fact]
    public async Task Search_MissingStationForMatchedRoute_ReturnsEmptyResult()
    {
        var fixture = SearchFixture.Create();
        fixture.Stations.Remove(fixture.DestinationStation);
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(fixture.Query, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Search_ByLocationCodes_MapsToRouteStations()
    {
        var fixture = SearchFixture.Create();
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var query = new SearchTripsQuery(
            null,
            null,
            new DateOnly(2026, 5, 18),
            1,
            false,
            "HCM",
            "HN");

        var result = await fixture.Handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle()
            .Which.TripId.Should().Be(trip.Id);
    }

    [Fact]
    public async Task GetSeatMap_UsesGeometryFromVehicleSeatLayoutJson()
    {
        var operatorId = Guid.NewGuid();
        var vehicleType = VehicleType.Create("SLEEPER_BUS", "Sleeper bus", null, 2, true);
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            "51B-12345",
            JsonSerializer.SerializeToElement(new SeatLayoutDto(
                1,
                "SLEEPER_BUS",
                2,
                8,
                4,
                2,
                [new SeatLayoutAisleDto(2)],
                [
                    new SeatLayoutSeatDto("A01", 7, 3, 2, "SLEEPER_LOWER", true, false, false),
                    new SeatLayoutSeatDto("A02", 8, 4, 2, "SLEEPER_UPPER", true, false, false),
                ])),
            2,
            null,
            null);
        var trip = DomainTrip.Create(
            operatorId,
            Guid.NewGuid(),
            vehicle.Id,
            Guid.NewGuid(),
            null,
            null,
            DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"),
            DateTimeOffset.Parse("2026-05-18T14:00:00+07:00"),
            TripSource.AUTO_FROM_SCHEDULE,
            Money.FromRaw(400000),
            null,
            0m);
        var handler = new GetTripSeatMapHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryTripSeatRepository([TripSeat.Create(trip.Id, "A01")]),
            new InMemoryVehicleRepository([vehicle]),
            new InMemoryVehicleTypeRepository([vehicleType]));

        var result = await handler.Handle(new GetTripSeatMapQuery(trip.Id), CancellationToken.None);

        result.Seats.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TripSeatMapSeatDto("A01", "AVAILABLE", "SLEEPER_LOWER", 7, 3, 2));
    }

    [Fact]
    public async Task GetDetail_ProjectsPersistedStopAndDestinationArrivalState()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Bến xe Miền Đông", "ben-xe-mien-dong", "Hồ Chí Minh", "Hồ Chí Minh");
        var destination = Station.Create("Bến xe Đà Lạt", "ben-xe-da-lat", "Đà Lạt", "Lâm Đồng");
        var route = Route.Create(operatorId, "HCM - Đà Lạt", origin.Id, destination.Id, Money.FromRaw(400000), 310m, 420);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.Parse("2026-07-21T01:00:00Z"));
        var destinationArrivedAt = DateTimeOffset.Parse("2026-07-21T08:30:00Z");
        trip.MarkDestinationArrived(destinationArrivedAt, Guid.NewGuid());

        var pendingStop = Stop.Create(operatorId, "Điểm chờ", 10.1m, 106.1m);
        var arrivedStop = Stop.Create(operatorId, "Điểm đã đến", 11.1m, 107.1m);
        var skippedStop = Stop.Create(operatorId, "Điểm bỏ qua", 12.1m, 108.1m);
        var pending = TripStop.Create(trip.Id, pendingStop.Id, 1, trip.DepartureDateTime.AddHours(1), true, true, 40m);
        var arrived = TripStop.Create(trip.Id, arrivedStop.Id, 2, trip.DepartureDateTime.AddHours(2), true, true, 80m);
        var skipped = TripStop.Create(trip.Id, skippedStop.Id, 3, trip.DepartureDateTime.AddHours(3), true, true, 120m);
        var stopArrivedAt = DateTimeOffset.Parse("2026-07-21T03:05:00Z");
        arrived.MarkArrived(stopArrivedAt);
        skipped.MarkSkipped();

        var handler = new GetTripDetailHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryStopRepository([pendingStop, arrivedStop, skippedStop]),
            new InMemoryTripSeatRepository([]),
            new InMemoryTripStopRepository([skipped, arrived, pending]),
            new InMemoryTripStopFareRepository([]));

        var result = await handler.Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.DestinationArrivedAt.Should().Be(destinationArrivedAt);
        result.Stops.Select(stop => stop.Status).Should().Equal("PENDING", "ARRIVED", "SKIPPED");
        result.Stops[0].ActualArrivalTime.Should().BeNull();
        result.Stops[1].ActualArrivalTime.Should().Be(stopArrivedAt);
        result.Stops[2].ActualArrivalTime.Should().BeNull();
    }

    private static DomainTrip CreateTrip(Guid operatorId, Guid routeId, DateTimeOffset departure)
    {
        return DomainTrip.Create(
            operatorId,
            routeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(6),
            TripSource.AUTO_FROM_SCHEDULE,
            Money.FromRaw(400000),
            null,
            0m);
    }

    private sealed class SearchFixture
    {
        private SearchFixture(string operatorName)
        {
            OperatorId = Guid.NewGuid();
            OriginLocation = Location.Create("HCM", "Ho Chi Minh City", Location.MunicipalityType, 5);
            DestinationLocation = Location.Create("HN", "Ha Noi", Location.MunicipalityType, 1);
            OriginStation = Station.Create("Bến xe Miền Đông", "ben-xe-mien-dong", "Hồ Chí Minh", "Hồ Chí Minh");
            DestinationStation = Station.Create("Bến xe Mỹ Đình", "ben-xe-my-dinh", "Hà Nội", "Hà Nội");
            OriginStation.UpdateProfile(
                OriginStation.Name,
                OriginStation.Slug,
                OriginStation.City,
                OriginStation.Province,
                OriginStation.AddressStreet,
                OriginLocation.Id,
                OriginStation.Latitude,
                OriginStation.Longitude,
                OriginStation.ContactPhone,
                OriginStation.ContactEmail,
                OriginStation.OperatingHours,
                OriginStation.Facilities,
                OriginStation.SupportsShuttle);
            DestinationStation.UpdateProfile(
                DestinationStation.Name,
                DestinationStation.Slug,
                DestinationStation.City,
                DestinationStation.Province,
                DestinationStation.AddressStreet,
                DestinationLocation.Id,
                DestinationStation.Latitude,
                DestinationStation.Longitude,
                DestinationStation.ContactPhone,
                DestinationStation.ContactEmail,
                DestinationStation.OperatingHours,
                DestinationStation.Facilities,
                DestinationStation.SupportsShuttle);
            Route = Route.Create(OperatorId, "HCM - HN", OriginStation.Id, DestinationStation.Id, Money.FromRaw(400000), 1000m, 720);
            Stations.AddRange([OriginStation, DestinationStation]);
            Locations.AddRange([OriginLocation, DestinationLocation]);
            Identity = new FakeIdentityInternalClient(new Dictionary<Guid, string> { [OperatorId] = operatorName });
            Handler = new SearchTripsHandler(
                new InMemoryTripRepository(Trips),
                new InMemoryRouteRepository([Route]),
                new InMemoryStationRepository(Stations),
                new InMemoryTripSeatRepository(Seats),
                new InMemoryTripStopRepository(Stops),
                new InMemoryLocationRepository(Locations),
                Identity);
            Query = new SearchTripsQuery(OriginStation.Id, DestinationStation.Id, new DateOnly(2026, 5, 18), 1, false);
        }

        public Guid OperatorId { get; }
        public Location OriginLocation { get; }
        public Location DestinationLocation { get; }
        public Station OriginStation { get; }
        public Station DestinationStation { get; }
        public Route Route { get; }
        public List<Location> Locations { get; } = [];
        public List<Station> Stations { get; } = [];
        public List<DomainTrip> Trips { get; } = [];
        public List<TripSeat> Seats { get; } = [];
        public List<TripStop> Stops { get; } = [];
        public FakeIdentityInternalClient Identity { get; }
        public SearchTripsHandler Handler { get; }
        public SearchTripsQuery Query { get; }

        public static SearchFixture Create(string operatorName = "VietRide Express") => new(operatorName);
    }

    private sealed class FakeIdentityInternalClient : IIdentityInternalClient
    {
        private readonly IReadOnlyDictionary<Guid, string> operatorNames;

        public FakeIdentityInternalClient(IReadOnlyDictionary<Guid, string> operatorNames)
        {
            this.operatorNames = operatorNames;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IdentityUserLookupResult.ValidationFailure("User lookup is not used by these tests."));

        public Task<IdentityOperatorLookupResult> GetOperatorAsync(Guid operatorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(operatorNames.TryGetValue(operatorId, out var name)
                ? IdentityOperatorLookupResult.Success(operatorId, name)
                : IdentityOperatorLookupResult.ValidationFailure($"Operator '{operatorId}' was not found in Identity."));
    }

    private sealed class FakeBookingImpactClient(TripBookingImpactProjection projection) : IBookingImpactClient
    {
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken) =>
            Task.FromResult(projection);
    }

    private sealed class FakeParcelImpactClient(TripParcelCancellationImpactProjection projection) : IParcelImpactClient
    {
        public Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken) =>
            Task.FromResult(projection);
    }

    private abstract class InMemoryRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly List<TEntity> items;
        private readonly Func<TEntity, TId> idSelector;

        protected InMemoryRepository(List<TEntity> items, Func<TEntity, TId> idSelector)
        {
            this.items = items;
            this.idSelector = idSelector;
        }

        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct) =>
            Task.FromResult(items.FirstOrDefault(item => EqualityComparer<TId>.Default.Equals(idSelector(item), id)));

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken ct)
        {
            items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity) { }

        public void Remove(TEntity entity) => items.Remove(entity);

        public IQueryable<TEntity> Query() => items.AsQueryable();

        public IQueryable<TEntity> QueryNoTracking() => items.AsQueryable();
    }

    private sealed class InMemoryTripRepository : InMemoryRepository<DomainTrip, Guid>, ITripRepository
    {
        public InMemoryTripRepository(List<DomainTrip> trips)
            : base(trips, trip => trip.Id) { }

        public Task<DomainTrip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class InMemoryRouteRepository : InMemoryRepository<Route, Guid>, IRouteRepository
    {
        public InMemoryRouteRepository(List<Route> routes)
            : base(routes, route => route.Id) { }

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId));

        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));

        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<Route>)Query().Where(route => route.OperatorId == operatorId).ToList());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().Any(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));
    }

    private sealed class InMemoryLocationRepository : InMemoryRepository<Location, Guid>, ILocationRepository
    {
        public InMemoryLocationRepository(List<Location> locations)
            : base(locations, location => location.Id) { }

        public Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(location => location.Id == id && location.IsActive));

        public Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(location =>
                location.Code == code.Trim().ToUpperInvariant() && location.IsActive));

        public Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().Any(location =>
                location.Code == code.Trim().ToUpperInvariant()
                && (!exceptId.HasValue || location.Id != exceptId.Value)));

        public Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult((IReadOnlyList<Location>)Query()
                .Where(location => location.IsActive)
                .OrderBy(location => location.SortOrder)
                .ThenBy(location => location.Name)
                .ToList());

        public Task<PagedResult<Location>> ListAsync(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryStationRepository : InMemoryRepository<Station, Guid>, IStationRepository
    {
        public InMemoryStationRepository(List<Station> stations)
            : base(stations, station => station.Id) { }

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string? q,
            string? city,
            string? province,
            Guid? locationId,
            CancellationToken cancellationToken)
        {
            var stations = Query();
            if (!string.IsNullOrWhiteSpace(q))
            {
                stations = stations.Where(station => station.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (locationId.HasValue)
            {
                stations = stations.Where(station => station.LocationId == locationId.Value);
            }

            return Task.FromResult((IReadOnlyList<Station>)stations.ToList());
        }
    }

    private sealed class InMemoryTripSeatRepository : InMemoryRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public InMemoryTripSeatRepository(List<TripSeat> seats)
            : base(seats, seat => seat.Id) { }
    }

    private sealed class InMemoryTripStopRepository : InMemoryRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public InMemoryTripStopRepository(List<TripStop> stops)
            : base(stops, stop => (stop.TripId, stop.StopId)) { }
    }

    private sealed class InMemoryStopRepository : InMemoryRepository<Stop, Guid>, IStopRepository
    {
        public InMemoryStopRepository(List<Stop> stops)
            : base(stops, stop => stop.Id) { }
    }

    private sealed class InMemoryTripStopFareRepository : InMemoryRepository<TripStopFare, (Guid TripId, Guid StopId)>, ITripStopFareRepository
    {
        public InMemoryTripStopFareRepository(List<TripStopFare> fares)
            : base(fares, fare => (fare.TripId, fare.StopId)) { }
    }

    private sealed class InMemoryVehicleRepository : InMemoryRepository<Vehicle, Guid>, IVehicleRepository
    {
        public InMemoryVehicleRepository(List<Vehicle> vehicles)
            : base(vehicles, vehicle => vehicle.Id) { }

        public Task<Vehicle?> GetOwnedByIdAsync(Guid operatorId, Guid vehicleId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(vehicle => vehicle.OperatorId == operatorId && vehicle.Id == vehicleId));

        public Task<PagedResult<Vehicle>> ListByOperatorAsync(
            Guid operatorId,
            int page,
            int pageSize,
            string? search,
            string? searchIn,
            string? sortBy,
            string sortDir,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> LicensePlateExistsAsync(string licensePlate, Guid? excludedVehicleId, CancellationToken cancellationToken) =>
            Task.FromResult(Query().Any(vehicle => vehicle.LicensePlate == licensePlate && vehicle.Id != excludedVehicleId));

        public Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class InMemoryVehicleTypeRepository : InMemoryRepository<VehicleType, Guid>, IVehicleTypeRepository
    {
        public InMemoryVehicleTypeRepository(List<VehicleType> vehicleTypes)
            : base(vehicleTypes, vehicleType => vehicleType.Id) { }

        public Task<VehicleType?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(vehicleType => vehicleType.Id == id && vehicleType.IsActive));

        public Task<PagedResult<VehicleType>> ListActiveAsync(
            int page,
            int pageSize,
            string? search,
            string? searchIn,
            string? sortBy,
            string sortDir,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
