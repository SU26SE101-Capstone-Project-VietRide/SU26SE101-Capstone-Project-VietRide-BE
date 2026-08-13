using FluentAssertions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Locations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.Locations;

public sealed class ListAdminLocationsHandlerTests
{
    [Fact]
    public async Task ParentCode_AcceptsInactiveTopLevelParentAndCombinesTypeBeforePaging()
    {
        var parent = Location.Create("79", "Ho Chi Minh City", Location.MunicipalityType, 1, isActive: false);
        var child = Location.Create("26734", "Ben Nghe", Location.WardType, parent.Id, 1);
        var repository = StubDispatchProxy<ILocationRepository>.Create();
        repository.SetResult(nameof(ILocationRepository.QueryNoTracking), new[] { parent, child }.AsQueryable());
        repository.SetResult(nameof(ILocationRepository.ListAsync), PagedResult<Location>.Create([child], 1, 20, 1));
        var handler = new ListAdminLocationsHandler(repository.Object);

        var result = await handler.Handle(
            new ListAdminLocationsQuery(1, 20, null, null, "ward", "79"),
            CancellationToken.None);

        result.TotalItems.Should().Be(1);
        result.Items.Should().ContainSingle(item => item.Code == child.Code && item.ParentCode == parent.Code);
        repository.CallCount(nameof(ILocationRepository.ListAsync)).Should().Be(1);
        repository.LastArguments(nameof(ILocationRepository.ListAsync))![5].Should().Be(Location.WardType);
        repository.LastArguments(nameof(ILocationRepository.ListAsync))![6].Should().Be(parent.Id);
    }
}
