using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class ListDriverSchedulesHandlerTests
{
    [Fact]
    public async Task Search_MatchesRouteVehicleDriverAndAssistantBeforeCountAndPaging()
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", "origin", "Ha Noi", "Ward 1");
        var destination = Station.Create("Destination", "destination", "Da Nang", "Ward 2");
        var routeMatch = Route.Create(
            operatorId,
            "Needle Express",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100m,
            180);
        var neutralRoute = Route.Create(
            operatorId,
            "Coastal Express",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100m,
            180);
        var vehicleMatch = VehicleTestData.CreateVehicle(operatorId);
        vehicleMatch.ChangeLicensePlate("51A-NEEDLE");
        var matchingDriverId = Guid.NewGuid();
        var matchingAssistantId = Guid.NewGuid();
        var schedules = new[]
        {
            CreateSchedule(operatorId, routeMatch.Id, null, Guid.NewGuid(), null),
            CreateSchedule(operatorId, neutralRoute.Id, vehicleMatch.Id, Guid.NewGuid(), null),
            CreateSchedule(operatorId, neutralRoute.Id, null, matchingDriverId, null),
            CreateSchedule(operatorId, neutralRoute.Id, null, Guid.NewGuid(), matchingAssistantId),
            CreateSchedule(operatorId, neutralRoute.Id, null, Guid.NewGuid(), null),
        };
        var scheduleRepository = StubDispatchProxy<IDriverScheduleRepository>.Create();
        scheduleRepository.SetResult(nameof(IDriverScheduleRepository.QueryNoTracking), schedules.AsQueryable());
        var routeRepository = StubDispatchProxy<IRouteRepository>.Create();
        routeRepository.SetResult(
            nameof(IRouteRepository.QueryNoTracking),
            new[] { routeMatch, neutralRoute }.AsQueryable());
        var vehicleRepository = StubDispatchProxy<IVehicleRepository>.Create();
        vehicleRepository.SetResult(nameof(IVehicleRepository.QueryNoTracking), new[] { vehicleMatch }.AsQueryable());
        var stationRepository = StubDispatchProxy<IStationRepository>.Create();
        stationRepository.SetResult(
            nameof(IStationRepository.QueryNoTracking),
            new[] { origin, destination }.AsQueryable());
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        identity.SetResult(
            nameof(IIdentityInternalClient.SearchOperatorCrewAsync),
            IdentityCrewSearchResult.Success(
            [
                new IdentityCrewProfile(matchingDriverId, "Needle Driver", "DRIVER"),
                new IdentityCrewProfile(matchingAssistantId, "Needle Assistant", "ASSISTANT"),
            ]));
        identity.SetResult(
            nameof(IIdentityInternalClient.GetUsersAsync),
            new Dictionary<Guid, IdentityUserProfile>());
        var handler = new ListDriverSchedulesHandler(
            scheduleRepository.Object,
            routeRepository.Object,
            vehicleRepository.Object,
            stationRepository.Object,
            identity.Object);

        var result = await handler.Handle(
            new ListDriverSchedulesQuery(operatorId, 1, 20, null, null, null, "needle"),
            CancellationToken.None);

        result.TotalItems.Should().Be(4);
        result.Items.Select(item => item.Id).Should().BeEquivalentTo(schedules.Take(4).Select(item => item.Id));
    }

    private static DriverSchedule CreateSchedule(
        Guid operatorId,
        Guid routeId,
        Guid? vehicleId,
        Guid driverId,
        Guid? assistantId) => DriverSchedule.Create(
            operatorId,
            routeId,
            vehicleId,
            driverId,
            assistantId,
            JsonSerializer.SerializeToElement(new[] { 1 }),
            new TimeOnly(8, 0),
            new DateOnly(2026, 8, 13),
            null,
            true);
}
