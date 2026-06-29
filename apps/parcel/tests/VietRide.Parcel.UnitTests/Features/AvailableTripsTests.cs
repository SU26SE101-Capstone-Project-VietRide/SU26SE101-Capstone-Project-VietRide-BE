using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.AvailableTrips;
using VietRide.Shared.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class AvailableTripsTests
{
    private static readonly Guid OriginStationId = Guid.NewGuid();
    private static readonly Guid DestinationStationId = Guid.NewGuid();
    private static readonly DateOnly DepartureDate = new(2026, 7, 15);
    private const decimal WeightKg = 5.5m;
    private const string SizeCategory = "MEDIUM";
    private const int Page = 1;
    private const int PageSize = 20;
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Departure = new(2026, 7, 15, 8, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public async Task Handle_ReturnsEnrichedTrips_WhenTripSearchSucceeds()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var tripDto = new ParcelTripDto(
            TripId, RouteId, OperatorId, "Test Operator",
            Departure, 50.0m, 200_000);

        tripClient.SearchAvailableParcelTripsAsync(
                OriginStationId, DestinationStationId, DepartureDate,
                WeightKg, Arg.Any<ParcelSizeCategory>(), Page, PageSize,
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                new List<ParcelTripDto> { tripDto },
                1, Page, PageSize, null));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fare.CreatedAt = Now;
        fare.UpdatedAt = Now;
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items.Single();
        item.TripId.Should().Be(TripId);
        item.RouteId.Should().Be(RouteId);
        item.OperatorName.Should().Be("Test Operator");
        item.DepartureDateTime.Should().Be(Departure);
        item.AvailableCargoWeightKg.Should().Be(50.0m);
        item.PriceVnd.Should().Be(150_000);
        result.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ThrowsTripSearchUnavailable_WhenUpstreamFails()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.TransportError,
                null, 0, Page, PageSize, "upstream timeout"));

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var ex = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("TRIP_SEARCH_UNAVAILABLE");
    }

    [Fact]
    public async Task Handle_SkipsTrips_WithoutConfiguredFare()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var routeWithFare = Guid.NewGuid();
        var routeWithoutFare = Guid.NewGuid();

        var trips = new List<ParcelTripDto>
        {
            new(Guid.NewGuid(), routeWithFare, OperatorId, "Op A",
                Departure, 30.0m, 100_000),
            new(Guid.NewGuid(), routeWithoutFare, OperatorId, "Op B",
                Departure.AddHours(1), 20.0m, 80_000),
        };

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                trips, 2, Page, PageSize, null));

        var fare = ParcelRouteFare.Create(routeWithFare, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fareRepo.FindByCompositeAsync(routeWithFare, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);
        fareRepo.FindByCompositeAsync(routeWithoutFare, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns((ParcelRouteFare?)null);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items.Single().OperatorName.Should().Be("Op A");
        result.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Handle_EnrichesOperatorName_WhenEmptyInDto()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var tripDto = new ParcelTripDto(
            TripId, RouteId, OperatorId, "",
            Departure, 50.0m, 200_000);

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                new List<ParcelTripDto> { tripDto },
                1, Page, PageSize, null));

        identityClient.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(OperatorId, "Enriched Operator"),
                null));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items.Single().OperatorName.Should().Be("Enriched Operator");

        await identityClient.Received(1).GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ThrowsOperatorLookupUnavailable_WhenIdentityFails()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var tripDto = new ParcelTripDto(
            TripId, RouteId, OperatorId, "",
            Departure, 50.0m, 200_000);

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                new List<ParcelTripDto> { tripDto },
                1, Page, PageSize, null));

        identityClient.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.TransportError,
                null, "identity unreachable"));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var ex = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("OPERATOR_LOOKUP_UNAVAILABLE");
    }

    [Fact]
    public async Task Handle_InvalidSizeCategory_ThrowsValidationError()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, "TINY", Page, PageSize);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("INVALID_SIZE_CATEGORY");
    }

    [Fact]
    public async Task Handle_OperatorNotFound_ThrowsOperatorNotFound()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var tripDto = new ParcelTripDto(
            TripId, RouteId, OperatorId, "",
            Departure, 50.0m, 200_000);

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                new List<ParcelTripDto> { tripDto },
                1, Page, PageSize, null));

        identityClient.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.OperatorNotFound,
                null, null));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var ex = await Assert.ThrowsAsync<CodedNotFoundException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("OPERATOR_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_OperatorForbidden_ThrowsOperatorLookupUnavailable()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var tripDto = new ParcelTripDto(
            TripId, RouteId, OperatorId, "",
            Departure, 50.0m, 200_000);

        tripClient.SearchAvailableParcelTripsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<decimal>(), Arg.Any<ParcelSizeCategory>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelTripSearchOutcome(
                ParcelTripSearchOutcomeKind.Success,
                new List<ParcelTripDto> { tripDto },
                1, Page, PageSize, null));

        identityClient.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Forbidden,
                null, "forbidden"));

        var fare = ParcelRouteFare.Create(RouteId, ParcelSizeCategory.MEDIUM, OperatorId,
            Money.FromRaw(150_000), Now);
        fareRepo.FindByCompositeAsync(RouteId, ParcelSizeCategory.MEDIUM, Arg.Any<CancellationToken>())
            .Returns(fare);

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, PageSize);

        var ex = await Assert.ThrowsAsync<ParcelDependencyUnavailableException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("OPERATOR_LOOKUP_UNAVAILABLE");
    }

    [Fact]
    public async Task Handle_PageZero_ThrowsValidationError()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, 0, PageSize);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Handle_PageSizeZero_ThrowsValidationError()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, 0);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Handle_PageSizeExceedsMax_ThrowsValidationError()
    {
        var tripClient = Substitute.For<ITripServiceClient>();
        var identityClient = Substitute.For<IIdentityServiceClient>();
        var fareRepo = Substitute.For<IParcelRouteFareRepository>();

        var handler = new AvailableTripsQueryHandler(tripClient, identityClient, fareRepo);
        var query = new AvailableTripsQuery(
            OriginStationId, DestinationStationId, DepartureDate,
            WeightKg, SizeCategory, Page, 101);

        var ex = await Assert.ThrowsAsync<CodedValidationException>(() =>
            handler.Handle(query, CancellationToken.None));
        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
    }
}
