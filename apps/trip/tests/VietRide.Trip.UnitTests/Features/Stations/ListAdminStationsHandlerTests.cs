using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.Stations;

public sealed class ListAdminStationsHandlerTests
{
    [Fact]
    public async Task List_FiltersSupportsShuttleBeforeCountAndPaging()
    {
        var shuttle = Station.Create("Shuttle Station", "shuttle", "Ha Noi", "Ward 1", supportsShuttle: true);
        var regular = Station.Create("Regular Station", "regular", "Ha Noi", "Ward 2");
        var repository = StubDispatchProxy<IStationRepository>.Create();
        repository.SetResult(nameof(IStationRepository.QueryNoTracking), new[] { shuttle, regular }.AsQueryable());

        var result = await new ListAdminStationsHandler(repository.Object).Handle(
            new ListAdminStationsQuery(1, 20, null, null, true, "name", "asc"),
            CancellationToken.None);

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.Id == shuttle.Id);
    }

    [Fact]
    public async Task Summary_CountsActiveInactiveAndShuttleIndependently()
    {
        var activeShuttle = Station.Create("Active Shuttle", "active-shuttle", "Ha Noi", "Ward 1", supportsShuttle: true);
        var inactiveShuttle = Station.Create("Inactive Shuttle", "inactive-shuttle", "Ha Noi", "Ward 2", supportsShuttle: true);
        inactiveShuttle.Deactivate();
        var activeRegular = Station.Create("Active Regular", "active-regular", "Ha Noi", "Ward 3");
        var repository = StubDispatchProxy<IStationRepository>.Create();
        repository.SetResult(
            nameof(IStationRepository.QueryNoTracking),
            new[] { activeShuttle, inactiveShuttle, activeRegular }.AsQueryable());

        var result = await new GetAdminStationSummaryHandler(repository.Object).Handle(
            new GetAdminStationSummaryQuery(),
            CancellationToken.None);

        result.Should().Be(new AdminStationSummaryDto(3, 2, 1, 2));
    }
}
