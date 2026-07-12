using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.DriverSchedules;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.DriverTrips;

public sealed class GetAssignedTripRouteHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_AssignedCrew_ReturnsPolylineAndOrderedWaypoints(bool callerIsDriver)
    {
        var callerId = Guid.NewGuid();
        var otherCrewId = Guid.NewGuid();
        var trip = CreateTrip(
            callerIsDriver ? callerId : otherCrewId,
            callerIsDriver ? otherCrewId : callerId);
        var expected = CreateRouteDto(trip.Id, trip.RouteId, "_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(nameof(ITripRepository.GetByIdAsync), trip);
        repository.SetResult(nameof(ITripRepository.GetDriverTripRouteAsync), expected);
        var handler = new GetAssignedTripRouteHandler(repository.Object);

        var result = await handler.Handle(
            new GetAssignedTripRouteQuery(trip.Id, callerId),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        result.PathPolyline.Should().Be(expected.PathPolyline);
        result.Stops.Select(stop => stop.OrderIndex).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Handle_UnassignedUser_ThrowsForbiddenWithoutReadingRoute()
    {
        var trip = CreateTrip(Guid.NewGuid(), Guid.NewGuid());
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(nameof(ITripRepository.GetByIdAsync), trip);
        var handler = new GetAssignedTripRouteHandler(repository.Object);

        var action = () => handler.Handle(
            new GetAssignedTripRouteQuery(trip.Id, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
        repository.CallCount(nameof(ITripRepository.GetDriverTripRouteAsync)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_UnknownTrip_ThrowsTripNotFound()
    {
        var repository = StubDispatchProxy<ITripRepository>.Create();
        var handler = new GetAssignedTripRouteHandler(repository.Object);

        var action = () => handler.Handle(
            new GetAssignedTripRouteQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<CodedNotFoundException>()
            .Where(exception => exception.ErrorCode == "TRIP_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_NullPolyline_ReturnsStationAndStopFallback()
    {
        var callerId = Guid.NewGuid();
        var trip = CreateTrip(callerId, null);
        var expected = CreateRouteDto(trip.Id, trip.RouteId, null);
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(nameof(ITripRepository.GetByIdAsync), trip);
        repository.SetResult(nameof(ITripRepository.GetDriverTripRouteAsync), expected);

        var result = await new GetAssignedTripRouteHandler(repository.Object).Handle(
            new GetAssignedTripRouteQuery(trip.Id, callerId),
            CancellationToken.None);

        result.PathPolyline.Should().BeNull();
        result.OriginStation.Latitude.Should().BeNull();
        result.DestinationStation.Longitude.Should().BeNull();
        result.Stops.Should().HaveCount(2);
    }

    [Fact]
    public async Task Validator_EmptyIds_ReturnsValidationErrors()
    {
        var result = await new GetAssignedTripRouteValidator().ValidateAsync(
            new GetAssignedTripRouteQuery(Guid.Empty, Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().OnlyContain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    private static TripEntity CreateTrip(Guid driverId, Guid? assistantId)
    {
        var departure = DateTimeOffset.Parse("2026-07-12T08:00:00+07:00");
        return TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            driverId,
            assistantId,
            null,
            departure,
            departure.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(250000),
            500m,
            0m);
    }

    private static DriverTripRouteDto CreateRouteDto(Guid tripId, Guid routeId, string? polyline) =>
        new(
            tripId,
            routeId,
            polyline,
            new DriverTripRouteStationDto(Guid.NewGuid(), "Origin", null, 106.7),
            new DriverTripRouteStationDto(Guid.NewGuid(), "Destination", 11.0, null),
            [
                new DriverTripRouteStopDto(Guid.NewGuid(), "Stop 1", 10.8, 106.8, 1, DateTimeOffset.UtcNow, true, false),
                new DriverTripRouteStopDto(Guid.NewGuid(), "Stop 2", 10.9, 106.9, 2, DateTimeOffset.UtcNow, false, true),
            ]);
}
