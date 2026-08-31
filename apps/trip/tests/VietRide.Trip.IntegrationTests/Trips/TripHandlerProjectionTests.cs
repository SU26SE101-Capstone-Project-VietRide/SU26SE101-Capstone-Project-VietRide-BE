using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Query;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.SearchTrips;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.Http;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class TripHandlerProjectionTests
{
    [Fact]
    public async Task TrackingRouteGeometry_ProjectsPolylineStationsAndIntermediateStops()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Bến đầu", "ben-dau", "Hồ Chí Minh", "Hồ Chí Minh", latitude: 10.7m, longitude: 106.6m);
        var destination = Station.Create("Bến cuối", "ben-cuoi", "Cần Thơ", "Cần Thơ", latitude: 10.0m, longitude: 105.7m);
        var route = Route.Create(operatorId, "Tuyến thử", origin.Id, destination.Id, Money.FromRaw(100_000), 100m, 120);
        route.SetPathGeometry("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.UtcNow.AddDays(1));
        var waypoint = Stop.Create(operatorId, "Điểm giữa", 10.5m, 106.2m);
        var tripStop = TripStop.Create(trip.Id, waypoint.Id, 1, trip.DepartureDateTime.AddMinutes(30), true, true, 50m);
        var handler = new GetTripRouteGeometryTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryTripStopRepository([tripStop]),
            new InMemoryStopRepository([waypoint]),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryAlternativeRouteRepository([], []));

        var result = await handler.Handle(new GetTripRouteGeometryTrackingQuery(trip.Id), CancellationToken.None);

        result.GeometrySource.Should().Be("ROUTE_POLYLINE");
        result.Points.Should().HaveCount(3);
        result.OriginStation.Should().BeEquivalentTo(new
        {
            StationId = origin.Id,
            origin.Name,
            Latitude = 10.7,
            Longitude = 106.6,
        });
        result.IntermediateStops.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            StopId = waypoint.Id,
            waypoint.Name,
            Sequence = 1,
            Latitude = 10.5,
            Longitude = 106.2,
        });
        result.DestinationStation.Should().NotBeNull();
        result.DestinationStation!.StationId.Should().Be(destination.Id);
        result.EffectiveRouteId.Should().Be(route.Id);
        result.TripStatus.Should().Be("SCHEDULED");
    }

    [Fact]
    public async Task TrackingRouteGeometry_AssignedAlternativeUsesTripStopSnapshotAndEffectiveDestination()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Main origin", "main-origin", "HCM", "HCM", latitude: 10.7m, longitude: 106.6m);
        var mainDestination = Station.Create("Main destination", "main-destination", "Can Tho", "Can Tho", latitude: 10.0m, longitude: 105.7m);
        var alternativeDestination = Station.Create("Alternative destination", "alternative-destination", "Da Lat", "Da Lat", latitude: 11.9m, longitude: 108.4m);
        var route = Route.Create(operatorId, "Main route", origin.Id, mainDestination.Id, Money.FromRaw(100_000), 100m, 120);
        route.SetPathGeometry("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        var alternative = AlternativeRoute.Create(route.Id, "Incident bypass", alternativeDestination.Id, 90m, 110);
        alternative.SetPathGeometry("_ulLnnqC_mqNvxq`@");
        var alternativeStop = Stop.Create(operatorId, "Alternative stop", 11.2m, 107.2m);
        var alternativeRouteStop = AlternativeRouteStop.Create(alternative.Id, alternativeStop.Id, 1, 45, null);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.UtcNow.AddDays(1));
        trip.ChangeAlternativeRoute(alternative.Id);
        var assignedSnapshotStop = Stop.Create(operatorId, "Assigned snapshot stop", 10.5m, 106.2m);
        var tripStop = TripStop.Create(trip.Id, assignedSnapshotStop.Id, 1, trip.DepartureDateTime.AddMinutes(30), true, true, 50m);
        var handler = new GetTripRouteGeometryTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryTripStopRepository([tripStop]),
            new InMemoryStopRepository([alternativeStop, assignedSnapshotStop]),
            new InMemoryStationRepository([origin, mainDestination, alternativeDestination]),
            new InMemoryAlternativeRouteRepository([alternative], [alternativeRouteStop]));

        var result = await handler.Handle(new GetTripRouteGeometryTrackingQuery(trip.Id), CancellationToken.None);

        result.EffectiveRouteId.Should().Be(alternative.Id);
        result.GeometrySource.Should().Be("ROUTE_POLYLINE");
        result.IntermediateStops.Should().ContainSingle().Which.StopId.Should().Be(assignedSnapshotStop.Id);
        result.IntermediateStops.Should().NotContain(stop => stop.StopId == alternativeStop.Id);
        result.DestinationStation!.StationId.Should().Be(alternativeDestination.Id);
    }

    [Fact]
    public async Task TrackingRouteGeometry_AlternativeWithoutPolylineDoesNotReuseMainRoutePolyline()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Main origin", "main-origin", "HCM", "HCM", latitude: 10.7m, longitude: 106.6m);
        var mainDestination = Station.Create("Main destination", "main-destination", "Can Tho", "Can Tho", latitude: 10.0m, longitude: 105.7m);
        var alternativeDestination = Station.Create("Alternative destination", "alternative-destination", "Da Lat", "Da Lat", latitude: 11.9m, longitude: 108.4m);
        var route = Route.Create(operatorId, "Main route", origin.Id, mainDestination.Id, Money.FromRaw(100_000), 100m, 120);
        route.SetPathGeometry("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        var alternative = AlternativeRoute.Create(route.Id, "Incident bypass", alternativeDestination.Id, 90m, 110);
        var alternativeStop = Stop.Create(operatorId, "Alternative stop", 11.2m, 107.2m);
        var alternativeRouteStop = AlternativeRouteStop.Create(alternative.Id, alternativeStop.Id, 1, 45, null);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.UtcNow.AddDays(1));
        trip.ChangeAlternativeRoute(alternative.Id);
        var handler = new GetTripRouteGeometryTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryTripStopRepository([]),
            new InMemoryStopRepository([alternativeStop]),
            new InMemoryStationRepository([origin, mainDestination, alternativeDestination]),
            new InMemoryAlternativeRouteRepository([alternative], [alternativeRouteStop]));

        var result = await handler.Handle(new GetTripRouteGeometryTrackingQuery(trip.Id), CancellationToken.None);

        result.GeometrySource.Should().Be("STOPS_ONLY");
        result.Points.Should().BeEmpty();
        result.IntermediateStops.Should().BeEmpty();
        result.DestinationStation!.StationId.Should().Be(alternativeDestination.Id);
    }

    [Fact]
    public async Task TrackingRouteGeometry_MissingAssignedAlternativeDoesNotFallbackToBaseRoute()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", "missing-alt-origin", "HCM", "HCM", latitude: 10.7m, longitude: 106.6m);
        var destination = Station.Create("Destination", "missing-alt-destination", "Can Tho", "Can Tho", latitude: 10.0m, longitude: 105.7m);
        var route = Route.Create(operatorId, "Base route", origin.Id, destination.Id, Money.FromRaw(100_000), 100m, 120);
        route.SetPathGeometry("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.UtcNow.AddDays(1));
        trip.ChangeAlternativeRoute(Guid.NewGuid());
        var handler = new GetTripRouteGeometryTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryTripStopRepository([]),
            new InMemoryStopRepository([]),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryAlternativeRouteRepository([], []));

        var act = () => handler.Handle(
            new GetTripRouteGeometryTrackingQuery(trip.Id),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "TRIP_NOT_FOUND");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("!")]
    public async Task TrackingRouteGeometry_MissingOrMalformedPolylineUsesStopOnlyFallback(string? pathPolyline)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Bến đầu", "ben-dau-fallback", "Hồ Chí Minh", "Hồ Chí Minh", latitude: 10.7m, longitude: 106.6m);
        var destination = Station.Create("Bến cuối", "ben-cuoi-fallback", "Cần Thơ", "Cần Thơ", latitude: 10.0m, longitude: 105.7m);
        var route = Route.Create(operatorId, "Tuyến fallback", origin.Id, destination.Id, Money.FromRaw(100_000), 100m, 120);
        route.SetPathGeometry(pathPolyline);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.UtcNow.AddDays(1));
        var waypoint = Stop.Create(operatorId, "Điểm giữa", 10.5m, 106.2m);
        var tripStop = TripStop.Create(trip.Id, waypoint.Id, 1, trip.DepartureDateTime.AddMinutes(30), true, true, 50m);
        var handler = new GetTripRouteGeometryTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryTripStopRepository([tripStop]),
            new InMemoryStopRepository([waypoint]),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryAlternativeRouteRepository([], []));

        var result = await handler.Handle(new GetTripRouteGeometryTrackingQuery(trip.Id), CancellationToken.None);

        result.GeometrySource.Should().Be("STOPS_ONLY");
        result.Points.Should().Equal(new RouteGeometryPointDto(10.5, 106.2));
        result.IntermediateStops.Should().ContainSingle();
    }

    [Fact]
    public async Task TrackingRouteStops_ProjectsSkippedStatus()
    {
        var operatorId = Guid.NewGuid();
        var trip = CreateTrip(operatorId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1));
        var waypoint = Stop.Create(operatorId, "Điểm bỏ qua", 10.5m, 106.2m);
        var tripStop = TripStop.Create(trip.Id, waypoint.Id, 1, trip.DepartureDateTime.AddMinutes(30), true, true, 50m);
        tripStop.MarkSkipped();
        var handler = new GetTripRouteStopsTrackingHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryTripStopRepository([tripStop]),
            new InMemoryStopRepository([waypoint]));

        var result = await handler.Handle(new GetTripRouteStopsTrackingQuery(trip.Id), CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.Status.Should().Be("SKIPPED");
    }

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
    public async Task CancelPreview_CanonicalParcelWireRefundContributesExactTotal()
    {
        var operatorId = Guid.NewGuid();
        var trip = CreateTrip(operatorId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1));
        var parcelId = Guid.NewGuid();
        using var httpClient = new HttpClient(new ParcelImpactResponseHandler(new
        {
            tripId = trip.Id,
            affectedParcels = new[]
            {
                new { parcelId, status = "RESERVED", refundAmountVnd = 135_000L },
            },
        }))
        {
            BaseAddress = new Uri("http://parcel"),
        };
        var parcelClient = new ParcelImpactClient(httpClient);
        var handler = new CancelTripPreviewQueryHandler(
            new InMemoryTripRepository([trip]),
            new FakeBookingImpactClient(new TripBookingImpactProjection(
                trip.Id,
                0,
                [])),
            parcelClient);

        var result = await handler.Handle(
            new CancelTripPreviewQuery(trip.Id, operatorId),
            CancellationToken.None);

        result.AffectedParcelIds.Should().Equal(parcelId);
        result.RefundTotalParcel.Should().Be(135_000);
        result.GrandTotal.Should().Be(135_000);
    }

    [Fact]
    public async Task Search_IncludesScheduledAndExcludesBoardingTrips()
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

        result.Items.Select(item => item.TripId).Should().Equal(scheduled.Id);
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
    public async Task Search_ProjectsOriginalAndEffectiveFareBreakdown()
    {
        var fixture = SearchFixture.Create(fareSurchargeService: new FixedFareSurchargeService(25));
        var trip = CreateTrip(
            fixture.OperatorId,
            fixture.Route.Id,
            DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(fixture.Query, CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Should().Match<SearchTripItem>(item =>
            item.BaseFare == 400_000
            && item.SurchargePercent == 25
            && item.SurchargeAmount == 100_000
            && item.EffectiveFare == 500_000
            && item.SurchargePeriodId.HasValue
            && item.SurchargePeriodName == "Holiday");
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
    public async Task Search_ByProvinceCodes_MapsToRouteStationsInAnyActiveChild()
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
            "79",
            null,
            "01",
            null);

        var result = await fixture.Handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle()
            .Which.TripId.Should().Be(trip.Id);
    }

    [Fact]
    public async Task Search_ByProvinceCodes_IncludesLegacyStationsAttachedDirectlyToRoot()
    {
        var fixture = SearchFixture.Create();
        fixture.OriginStation.UpdateProfile(
            fixture.OriginStation.Name,
            fixture.OriginStation.Slug,
            fixture.OriginStation.City,
            fixture.OriginStation.Ward,
            fixture.OriginStation.AddressStreet,
            fixture.OriginProvince.Id,
            fixture.OriginStation.Latitude,
            fixture.OriginStation.Longitude,
            fixture.OriginStation.ContactPhone,
            fixture.OriginStation.ContactEmail,
            fixture.OriginStation.OperatingHours,
            fixture.OriginStation.Facilities,
            fixture.OriginStation.SupportsShuttle);
        fixture.DestinationStation.UpdateProfile(
            fixture.DestinationStation.Name,
            fixture.DestinationStation.Slug,
            fixture.DestinationStation.City,
            fixture.DestinationStation.Ward,
            fixture.DestinationStation.AddressStreet,
            fixture.DestinationProvince.Id,
            fixture.DestinationStation.Latitude,
            fixture.DestinationStation.Longitude,
            fixture.DestinationStation.ContactPhone,
            fixture.DestinationStation.ContactEmail,
            fixture.DestinationStation.OperatingHours,
            fixture.DestinationStation.Facilities,
            fixture.DestinationStation.SupportsShuttle);
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(
            new SearchTripsQuery(
                null,
                null,
                new DateOnly(2026, 5, 18),
                1,
                false,
                fixture.OriginProvince.Code,
                null,
                fixture.DestinationProvince.Code,
                null),
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.TripId.Should().Be(trip.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Search_HierarchyMode_AllowsProvinceOnlyAndExactWardIndependently(
        bool exactOrigin,
        bool exactDestination)
    {
        var fixture = SearchFixture.Create();
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(
            new SearchTripsQuery(
                null,
                null,
                new DateOnly(2026, 5, 18),
                1,
                null,
                fixture.OriginProvince.Code,
                exactOrigin ? fixture.OriginLocation.Code : null,
                fixture.DestinationProvince.Code,
                exactDestination ? fixture.DestinationLocation.Code : null),
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.TripId.Should().Be(trip.Id);
    }

    [Fact]
    public async Task Search_HierarchyMode_RejectsWardFromAnotherProvince()
    {
        var fixture = SearchFixture.Create();

        var act = () => fixture.Handler.Handle(
            new SearchTripsQuery(
                null,
                null,
                new DateOnly(2026, 5, 18),
                1,
                null,
                fixture.OriginProvince.Code,
                fixture.DestinationLocation.Code,
                fixture.DestinationProvince.Code,
                null),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == nameof(SearchTripsQuery.OriginWardCode));
    }

    [Fact]
    public async Task Search_ByProvinceAndWardCodes_MatchesActiveStopsAndReturnsOnlyPointsInValidSequence()
    {
        var fixture = SearchFixture.Create();
        var pickupProvince = Location.Create("75", "Đồng Nai", Location.ProvinceType, 3);
        var pickupLocation = Location.Create("26188", "Phường Trấn Biên", Location.WardType, pickupProvince.Id, 1);
        var dropoffProvince = Location.Create("80", "Tây Ninh", Location.ProvinceType, 4);
        var dropoffLocation = Location.Create("27637", "Phường Tân Ninh", Location.WardType, dropoffProvince.Id, 1);
        var pickupStop = Stop.Create(
            fixture.OperatorId,
            "Pickup stop",
            10.7m,
            106.7m,
            address: "Pickup address",
            locationId: pickupLocation.Id);
        var ignoredPickupStop = Stop.Create(
            fixture.OperatorId,
            "Late pickup",
            10.8m,
            106.8m,
            locationId: pickupLocation.Id);
        var dropoffStop = Stop.Create(
            fixture.OperatorId,
            "Dropoff stop",
            10.9m,
            106.9m,
            address: "Dropoff address",
            locationId: dropoffLocation.Id);
        fixture.Locations.AddRange([pickupProvince, pickupLocation, dropoffProvince, dropoffLocation]);
        fixture.CanonicalStops.AddRange([pickupStop, ignoredPickupStop, dropoffStop]);
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));
        fixture.Stops.AddRange([
            TripStop.Create(trip.Id, pickupStop.Id, 1, trip.DepartureDateTime.AddHours(1), true, false, 10m),
            TripStop.Create(trip.Id, dropoffStop.Id, 2, trip.DepartureDateTime.AddHours(2), false, true, 20m),
            TripStop.Create(trip.Id, ignoredPickupStop.Id, 3, trip.DepartureDateTime.AddHours(3), true, false, 30m),
        ]);

        var result = await fixture.Handler.Handle(
            new SearchTripsQuery(null, null, new DateOnly(2026, 5, 18), 1, true, "75", "26188", "80", "27637"),
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.PickupPoints.Should().ContainSingle().Which.Should().Match<SearchTripPointDto>(point =>
            point.Type == "STOP"
            && point.StationId == null
            && point.StopId == pickupStop.Id
            && point.AllowPickup
            && !point.AllowDropoff);
        item.DropoffPoints.Should().ContainSingle().Which.Should().Match<SearchTripPointDto>(point =>
            point.Type == "STOP"
            && point.StationId == null
            && point.StopId == dropoffStop.Id
            && !point.AllowPickup
            && point.AllowDropoff);
    }

    [Fact]
    public async Task Search_ByHierarchyCodes_ExcludesTripWhenPickupIsNotBeforeDropoff()
    {
        var fixture = SearchFixture.Create();
        var pickupProvince = Location.Create("75", "Đồng Nai", Location.ProvinceType, 3);
        var pickupLocation = Location.Create("26188", "Phường Trấn Biên", Location.WardType, pickupProvince.Id, 1);
        var dropoffProvince = Location.Create("80", "Tây Ninh", Location.ProvinceType, 4);
        var dropoffLocation = Location.Create("27637", "Phường Tân Ninh", Location.WardType, dropoffProvince.Id, 1);
        var pickupStop = Stop.Create(fixture.OperatorId, "Pickup stop", 10.7m, 106.7m, locationId: pickupLocation.Id);
        var dropoffStop = Stop.Create(fixture.OperatorId, "Dropoff stop", 10.8m, 106.8m, locationId: dropoffLocation.Id);
        fixture.Locations.AddRange([pickupProvince, pickupLocation, dropoffProvince, dropoffLocation]);
        fixture.CanonicalStops.AddRange([pickupStop, dropoffStop]);
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));
        fixture.Stops.AddRange([
            TripStop.Create(trip.Id, dropoffStop.Id, 1, trip.DepartureDateTime.AddHours(1), false, true, 10m),
            TripStop.Create(trip.Id, pickupStop.Id, 2, trip.DepartureDateTime.AddHours(2), true, false, 20m),
        ]);

        var result = await fixture.Handler.Handle(
            new SearchTripsQuery(null, null, new DateOnly(2026, 5, 18), 1, null, "75", "26188", "80", "27637"),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pickup-not-allowed")]
    [InlineData("inactive-stop")]
    [InlineData("deleted-stop")]
    [InlineData("wrong-ward")]
    public async Task Search_ByHierarchyCodes_ExcludesIneligibleStops(string caseName)
    {
        var fixture = SearchFixture.Create();
        var pickupProvince = Location.Create("75", "Đồng Nai", Location.ProvinceType, 3);
        var pickupLocation = Location.Create("26188", "Phường Trấn Biên", Location.WardType, pickupProvince.Id, 1);
        var otherPickupLocation = Location.Create("26191", "Phường Tam Hiệp", Location.WardType, pickupProvince.Id, 2);
        var dropoffProvince = Location.Create("80", "Tây Ninh", Location.ProvinceType, 4);
        var dropoffLocation = Location.Create("27637", "Phường Tân Ninh", Location.WardType, dropoffProvince.Id, 1);
        var pickupStop = Stop.Create(
            fixture.OperatorId,
            "Pickup stop",
            10.7m,
            106.7m,
            locationId: caseName == "wrong-ward" ? otherPickupLocation.Id : pickupLocation.Id);
        var dropoffStop = Stop.Create(
            fixture.OperatorId,
            "Dropoff stop",
            10.8m,
            106.8m,
            locationId: dropoffLocation.Id);
        if (caseName == "inactive-stop") pickupStop.Deactivate();
        if (caseName == "deleted-stop") pickupStop.SoftDelete(DateTimeOffset.UtcNow);
        fixture.Locations.AddRange([
            pickupProvince,
            pickupLocation,
            otherPickupLocation,
            dropoffProvince,
            dropoffLocation,
        ]);
        fixture.CanonicalStops.AddRange([pickupStop, dropoffStop]);
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));
        fixture.Stops.AddRange([
            TripStop.Create(
                trip.Id,
                pickupStop.Id,
                1,
                trip.DepartureDateTime.AddHours(1),
                caseName != "pickup-not-allowed",
                caseName == "pickup-not-allowed",
                10m),
            TripStop.Create(trip.Id, dropoffStop.Id, 2, trip.DepartureDateTime.AddHours(2), false, true, 20m),
        ]);

        var result = await fixture.Handler.Handle(
            new SearchTripsQuery(
                null,
                null,
                new DateOnly(2026, 5, 18),
                1,
                null,
                "75",
                "26188",
                "80",
                "27637"),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_StationMode_RemainsExactAndIgnoresLegacyAlongRouteInput()
    {
        var fixture = SearchFixture.Create();
        var trip = CreateTrip(fixture.OperatorId, fixture.Route.Id, DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"));
        fixture.Trips.Add(trip);
        fixture.Seats.Add(TripSeat.Create(trip.Id, "A01"));

        var result = await fixture.Handler.Handle(
            fixture.Query with { AllowAlongRoutePickup = true },
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.PickupPoints.Should().ContainSingle().Which.StationId.Should().Be(fixture.OriginStation.Id);
        item.DropoffPoints.Should().ContainSingle().Which.StationId.Should().Be(fixture.DestinationStation.Id);
    }

    [Fact]
    public async Task GetSeatMap_UsesGeometryFromVehicleSeatLayoutJson()
    {
        var operatorId = Guid.NewGuid();
        var vehicleType = VehicleType.Create("SLEEPER_BUS", "Sleeper bus", null, 2, true);
        var webJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshotLayout = JsonSerializer.SerializeToElement(new SeatLayoutDto(
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
            ]), webJsonOptions);
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            "51B-12345",
            snapshotLayout,
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
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 0m,
            seatLayoutSnapshotJson: snapshotLayout);
        vehicle.UpdateSeatLayout(
            JsonSerializer.SerializeToElement(new SeatLayoutDto(
                1,
                "SLEEPER_BUS",
                2,
                8,
                4,
                2,
                [new SeatLayoutAisleDto(1)],
                [
                    new SeatLayoutSeatDto("A01", 7, 3, 2, "SLEEPER_LOWER", true, false, false),
                    new SeatLayoutSeatDto("A02", 8, 4, 2, "SLEEPER_UPPER", true, false, false),
                ]), webJsonOptions),
            2);
        var handler = new GetTripSeatMapHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryTripSeatRepository([TripSeat.Create(trip.Id, "A01")]),
            new InMemoryVehicleRepository([vehicle]),
            new InMemoryVehicleTypeRepository([vehicleType]));

        var result = await handler.Handle(new GetTripSeatMapQuery(trip.Id), CancellationToken.None);

        result.Seats.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TripSeatMapSeatDto("A01", "AVAILABLE", "SLEEPER_LOWER", 7, 3, 2));
        result.Aisles.Should().ContainSingle().Which.AfterCol.Should().Be(2);
    }

    [Theory]
    [InlineData(PlannedEtaSource.GOOGLE_ROUTES, "TRAFFIC_AWARE")]
    [InlineData(PlannedEtaSource.GOONG, "ROUTE_BASED")]
    [InlineData(PlannedEtaSource.ROUTE_BASELINE, "FALLBACK")]
    public async Task GetDetail_ProjectsPersistedStopAndDestinationArrivalState(
        PlannedEtaSource plannedEtaSource,
        string expectedQuality)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Bến xe Miền Đông", "ben-xe-mien-dong", "Hồ Chí Minh", "Hồ Chí Minh");
        var destination = Station.Create("Bến xe Đà Lạt", "ben-xe-da-lat", "Đà Lạt", "Lâm Đồng");
        var route = Route.Create(operatorId, "HCM - Đà Lạt", origin.Id, destination.Id, Money.FromRaw(400000), 310m, 420);
        var trip = CreateTrip(
            operatorId,
            route.Id,
            DateTimeOffset.Parse("2026-07-21T01:00:00Z"),
            plannedEtaSource);
        var destinationArrivedAt = DateTimeOffset.Parse("2026-07-21T08:30:00Z");
        trip.MarkDestinationArrived(destinationArrivedAt, Guid.NewGuid());

        var pendingStop = Stop.Create(operatorId, "Điểm chờ", 10.1m, 106.1m);
        var arrivedStop = Stop.Create(operatorId, "Điểm đã đến", 11.1m, 107.1m);
        var skippedStop = Stop.Create(operatorId, "Điểm bỏ qua", 12.1m, 108.1m);
        var pending = TripStop.Create(trip.Id, pendingStop.Id, 1, trip.DepartureDateTime.AddHours(1), true, true, 40m);
        var arrived = TripStop.Create(trip.Id, arrivedStop.Id, 2, trip.DepartureDateTime.AddHours(2), true, true, 80m);
        var skipped = TripStop.Create(trip.Id, skippedStop.Id, 3, trip.DepartureDateTime.AddHours(3), true, true, 120m);
        var stopArrivedAt = DateTimeOffset.Parse("2026-07-21T03:05:00Z");
        var stopDepartedAt = stopArrivedAt.AddMinutes(10);
        arrived.MarkArrived(stopArrivedAt);
        typeof(TripStop).GetProperty(nameof(TripStop.ActualDepartureTime))!
            .SetValue(arrived, stopDepartedAt);
        skipped.MarkSkipped();

        var handler = new GetTripDetailHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryAlternativeRouteRepository([], []),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryStopRepository([pendingStop, arrivedStop, skippedStop]),
            new InMemoryTripSeatRepository([]),
            new InMemoryTripStopRepository([skipped, arrived, pending]),
            new InMemoryTripStopFareRepository([]));

        var result = await handler.Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.DestinationArrivedAt.Should().Be(destinationArrivedAt);
        result.PlannedEtaQuality.Should().Be(expectedQuality);
        result.Stops.Select(stop => stop.Status).Should().Equal("PENDING", "ARRIVED", "SKIPPED");
        result.Stops[0].ActualArrivalTime.Should().BeNull();
        result.Stops[1].ActualArrivalTime.Should().Be(stopArrivedAt);
        result.Stops[1].ActualDepartureTime.Should().Be(stopDepartedAt);
        result.Stops[2].ActualArrivalTime.Should().BeNull();
    }

    [Fact]
    public async Task GetDetail_AppliesSameSurchargeToBaseAndStopOverride()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", "origin", "HCM", "HCM");
        var destination = Station.Create("Destination", "destination", "Da Lat", "Lam Dong");
        var route = Route.Create(
            operatorId, "HCM - Da Lat", origin.Id, destination.Id, Money.FromRaw(400_000), 310m, 420);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.Parse("2026-07-21T01:00:00Z"));
        var stop = Stop.Create(operatorId, "Pickup", 10.1m, 106.1m);
        var tripStop = TripStop.Create(
            trip.Id, stop.Id, 1, trip.DepartureDateTime.AddHours(1), true, true, 40m);
        var stopFare = TripStopFare.Create(
            trip.Id, stop.Id, Money.FromRaw(300_000), TripStopFareSource.MANUAL_OVERRIDE);
        var handler = new GetTripDetailHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryAlternativeRouteRepository([], []),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryStopRepository([stop]),
            new InMemoryTripSeatRepository([]),
            new InMemoryTripStopRepository([tripStop]),
            new InMemoryTripStopFareRepository([stopFare]),
            new FixedFareSurchargeService(25));

        var result = await handler.Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.BaseFare.Should().Be(400_000);
        result.EffectiveFare.Should().Be(500_000);
        result.FareBreakdown.EffectiveBaseFare.Should().Be(500_000);
        result.Stops.Should().ContainSingle().Which.Should().Match<TripStopDto>(item =>
            item.FareFromThisStop == 300_000
            && item.SurchargePercent == 25
            && item.SurchargeAmount == 75_000
            && item.EffectiveFare == 375_000);
        result.FareBreakdown.Stops.Should().ContainSingle().Which.EffectiveFareFromThisStop.Should().Be(375_000);
    }

    [Fact]
    public async Task GetDetail_ResolvesManualOverrideThenActiveTemplateThenBaseFare()
    {
        var now = DateTimeOffset.Parse("2026-07-20T01:00:00Z");
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", "origin", "HCM", "HCM");
        var destination = Station.Create("Destination", "destination", "Da Lat", "Lam Dong");
        var route = Route.Create(
            operatorId, "HCM - Da Lat", origin.Id, destination.Id, Money.FromRaw(400_000), 310m, 420);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.Parse("2026-07-21T01:00:00Z"));
        var manualStop = Stop.Create(operatorId, "Manual", 10.1m, 106.1m);
        var templateStop = Stop.Create(operatorId, "Template", 10.2m, 106.2m);
        var baseStop = Stop.Create(operatorId, "Base", 10.3m, 106.3m);
        var tripStops = new[]
        {
            TripStop.Create(trip.Id, manualStop.Id, 1, trip.DepartureDateTime.AddHours(1), true, true, 40m),
            TripStop.Create(trip.Id, templateStop.Id, 2, trip.DepartureDateTime.AddHours(2), true, true, 80m),
            TripStop.Create(trip.Id, baseStop.Id, 3, trip.DepartureDateTime.AddHours(3), true, true, 120m),
        };
        var manualFare = TripStopFare.Create(
            trip.Id, manualStop.Id, Money.FromRaw(350_000), TripStopFareSource.MANUAL_OVERRIDE);
        var templates = new[]
        {
            RouteStopFareTemplate.Create(
                route.Id, manualStop.Id, Money.FromRaw(300_000), now.AddDays(-1), now.AddDays(1)),
            RouteStopFareTemplate.Create(
                route.Id, templateStop.Id, Money.FromRaw(280_000), now.AddDays(-1), now.AddDays(1)),
        };
        var handler = new GetTripDetailHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryAlternativeRouteRepository([], []),
            new InMemoryStationRepository([origin, destination]),
            new InMemoryStopRepository([manualStop, templateStop, baseStop]),
            new InMemoryTripSeatRepository([]),
            new InMemoryTripStopRepository([.. tripStops]),
            new InMemoryTripStopFareRepository([manualFare]),
            null,
            new InMemoryRouteStopFareTemplateRepository([.. templates]),
            new FrozenClock(now));

        var result = await handler.Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.Stops.Select(stop => stop.EffectiveFare).Should().Equal(350_000, 280_000, 400_000);
        result.Stops.Select(stop => stop.FareFromThisStop).Should().Equal(350_000, 280_000, null);
    }

    [Fact]
    public async Task GetDetail_AssignedAlternative_ProjectsAlternativeIdAndDestination()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", "origin-alternative-detail", "HCM", "HCM");
        var mainDestination = Station.Create(
            "Main destination",
            "main-destination-alternative-detail",
            "Da Lat",
            "Lam Dong");
        var alternativeDestination = Station.Create(
            "Alternative destination",
            "alternative-destination-detail",
            "Nha Trang",
            "Khanh Hoa");
        var route = Route.Create(
            operatorId,
            "Main route",
            origin.Id,
            mainDestination.Id,
            Money.FromRaw(400_000),
            310m,
            420);
        var alternative = AlternativeRoute.Create(
            route.Id,
            "Incident bypass",
            alternativeDestination.Id,
            290m,
            390);
        var trip = CreateTrip(operatorId, route.Id, DateTimeOffset.Parse("2026-07-21T01:00:00Z"));
        trip.ChangeAlternativeRoute(alternative.Id);
        var handler = new GetTripDetailHandler(
            new InMemoryTripRepository([trip]),
            new InMemoryRouteRepository([route]),
            new InMemoryAlternativeRouteRepository([alternative], []),
            new InMemoryStationRepository([origin, mainDestination, alternativeDestination]),
            new InMemoryStopRepository([]),
            new InMemoryTripSeatRepository([]),
            new InMemoryTripStopRepository([]),
            new InMemoryTripStopFareRepository([]));

        var result = await handler.Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.AlternativeRouteId.Should().Be(alternative.Id);
        result.DestinationStation.Should().Be(new TripStationDto(
            alternativeDestination.Id,
            alternativeDestination.Name));
    }

    private static DomainTrip CreateTrip(
        Guid operatorId,
        Guid routeId,
        DateTimeOffset departure,
        PlannedEtaSource plannedEtaSource = PlannedEtaSource.ROUTE_BASELINE)
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
            null,
            0m,
            plannedEtaSource: plannedEtaSource);
    }

    private sealed class SearchFixture
    {
        private SearchFixture(string operatorName, IFareSurchargeService? fareSurchargeService)
        {
            OperatorId = Guid.NewGuid();
            OriginProvince = Location.Create("79", "Thành phố Hồ Chí Minh", Location.MunicipalityType, 5);
            OriginLocation = Location.Create("26734", "Phường Thủ Đức", Location.WardType, OriginProvince.Id, 1);
            DestinationProvince = Location.Create("01", "Thành phố Hà Nội", Location.MunicipalityType, 1);
            DestinationLocation = Location.Create("00070", "Phường Hoàn Kiếm", Location.WardType, DestinationProvince.Id, 1);
            OriginStation = Station.Create("Bến xe Miền Đông", "ben-xe-mien-dong", "Hồ Chí Minh", "Hồ Chí Minh");
            DestinationStation = Station.Create("Bến xe Mỹ Đình", "ben-xe-my-dinh", "Hà Nội", "Hà Nội");
            OriginStation.UpdateProfile(
                OriginStation.Name,
                OriginStation.Slug,
                OriginStation.City,
                OriginStation.Ward,
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
                DestinationStation.Ward,
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
            Locations.AddRange([OriginProvince, OriginLocation, DestinationProvince, DestinationLocation]);
            Identity = new FakeIdentityInternalClient(new Dictionary<Guid, string> { [OperatorId] = operatorName });
            Handler = new SearchTripsHandler(
                new InMemoryTripRepository(Trips),
                new InMemoryRouteRepository([Route]),
                new InMemoryStationRepository(Stations),
                new InMemoryTripSeatRepository(Seats),
                new InMemoryTripStopRepository(Stops),
                new InMemoryLocationRepository(Locations),
                Identity,
                fareSurchargeService,
                new InMemoryStopRepository(CanonicalStops));
            Query = new SearchTripsQuery(OriginStation.Id, DestinationStation.Id, new DateOnly(2026, 5, 18), 1, false);
        }

        public Guid OperatorId { get; }
        public Location OriginProvince { get; }
        public Location OriginLocation { get; }
        public Location DestinationProvince { get; }
        public Location DestinationLocation { get; }
        public Station OriginStation { get; }
        public Station DestinationStation { get; }
        public Route Route { get; }
        public List<Location> Locations { get; } = [];
        public List<Station> Stations { get; } = [];
        public List<DomainTrip> Trips { get; } = [];
        public List<TripSeat> Seats { get; } = [];
        public List<TripStop> Stops { get; } = [];
        public List<Stop> CanonicalStops { get; } = [];
        public FakeIdentityInternalClient Identity { get; }
        public SearchTripsHandler Handler { get; }
        public SearchTripsQuery Query { get; }

        public static SearchFixture Create(
            string operatorName = "VietRide Express",
            IFareSurchargeService? fareSurchargeService = null) => new(operatorName, fareSurchargeService);
    }

    private sealed class FixedFareSurchargeService(int percent) : IFareSurchargeService
    {
        private readonly FareSurchargeRule rule = new(Guid.NewGuid(), "Holiday", percent);

        public Task<FareSurchargeRule?> ResolveAsync(
            Guid operatorId,
            DateTimeOffset departureDateTime,
            CancellationToken cancellationToken = default) => Task.FromResult<FareSurchargeRule?>(rule);

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

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
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
        public Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
            Guid tripId,
            Guid stopId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => Task.FromResult(new ParcelStopDepartureClearanceProjection(
                tripId,
                stopId,
                operatorId,
                "CLEAR",
                [],
                null,
                null,
                null));

        public Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken) =>
            Task.FromResult(projection);

        public Task<ParcelTripCompletionClearanceProjection> GetTripCompletionClearanceAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => Task.FromResult(new ParcelTripCompletionClearanceProjection(
                tripId,
                operatorId,
                "CLEAR",
                [],
                []));
    }

    private sealed class ParcelImpactResponseHandler(object response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response),
            });
        }
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

        public IQueryable<TEntity> Query() => new TestAsyncEnumerable<TEntity>(items);

        public IQueryable<TEntity> QueryNoTracking() => new TestAsyncEnumerable<TEntity>(items);
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
        private readonly IEnumerator<T> inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            this.inner = inner;
        }

        public T Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider inner;

        public TestAsyncQueryProvider(IQueryProvider inner)
        {
            this.inner = inner;
        }

        public IQueryable CreateQuery(Expression expression) =>
            (IQueryable)Activator.CreateInstance(
                typeof(TestAsyncEnumerable<>).MakeGenericType(expression.Type.GetGenericArguments()[0]),
                expression)!;

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => inner.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(resultType)
                .Invoke(inner, [expression]);
            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [executionResult])!;
        }
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

    private sealed class InMemoryAlternativeRouteRepository(
        List<AlternativeRoute> routes,
        List<AlternativeRouteStop> stops)
        : InMemoryRepository<AlternativeRoute, Guid>(routes, route => route.Id), IAlternativeRouteRepository
    {
        public Task<AlternativeRoute?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid alternativeRouteId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Query().FirstOrDefault(route => route.Id == alternativeRouteId));

        public Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken) =>
            Task.FromResult(stops.Any(stop => stop.AlternativeRouteId == alternativeRouteId && stop.StopId == stopId));

        public Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken) =>
            Task.FromResult(stops.Any(stop => stop.AlternativeRouteId == alternativeRouteId && stop.OrderIndex == orderIndex));

        public Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(
            Guid alternativeRouteId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AlternativeRouteStop>>(stops
                .Where(stop => stop.AlternativeRouteId == alternativeRouteId)
                .OrderBy(stop => stop.OrderIndex)
                .ToArray());

        public Task ReplaceStopsAsync(
            Guid alternativeRouteId,
            IReadOnlyCollection<AlternativeRouteStop> replacementStops,
            CancellationToken cancellationToken)
        {
            stops.RemoveAll(stop => stop.AlternativeRouteId == alternativeRouteId);
            stops.AddRange(replacementStops);
            return Task.CompletedTask;
        }
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
            string? ward,
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

    private sealed class InMemoryRouteStopFareTemplateRepository
        : InMemoryRepository<RouteStopFareTemplate, Guid>, IRouteStopFareTemplateRepository
    {
        public InMemoryRouteStopFareTemplateRepository(List<RouteStopFareTemplate> templates)
            : base(templates, template => template.Id) { }

        public Task<bool> ExistsOverlappingAsync(
            Guid routeId,
            Guid stopId,
            DateTimeOffset effectiveFrom,
            DateTimeOffset? effectiveUntil,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(
            Guid routeId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(
                Query().Where(template => template.RouteId == routeId).ToArray());

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListActiveByRouteAsync(
            Guid routeId,
            DateTimeOffset pricingAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(Query()
                .Where(template => template.RouteId == routeId
                    && template.EffectiveFrom <= pricingAt
                    && (!template.EffectiveUntil.HasValue || pricingAt < template.EffectiveUntil.Value))
                .ToArray());
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
