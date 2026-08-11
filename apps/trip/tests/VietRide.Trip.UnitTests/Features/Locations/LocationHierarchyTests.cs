using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Features.Locations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Fakes;

namespace VietRide.Trip.UnitTests.Features.Locations;

public sealed class LocationHierarchyTests
{
    [Fact]
    public async Task ListLocations_WithoutParent_ReturnsOnlyActiveRoots()
    {
        var hcm = Location.Create("79", "Thành phố Hồ Chí Minh", Location.MunicipalityType, 1);
        var vungTau = Location.Create("26506", "Phường Vũng Tàu", Location.WardType, hcm.Id, 1);
        var inactiveProvince = Location.Create("01", "Thành phố Hà Nội", Location.MunicipalityType, 2);
        inactiveProvince.Deactivate();
        var handler = new ListLocationsHandler(TestLocationRepository.From(hcm, vungTau, inactiveProvince));

        var result = await handler.Handle(new ListLocationsQuery(null, null), CancellationToken.None);

        result.Should().ContainSingle().Which.Code.Should().Be("79");
        result[0].ParentId.Should().BeNull();
    }

    [Fact]
    public async Task ListLocations_ByParentAndSearch_ReturnsVungTauWithParentMetadata()
    {
        var hcm = Location.Create("79", "Thành phố Hồ Chí Minh", Location.MunicipalityType, 1);
        var vungTau = Location.Create("26506", "Phường Vũng Tàu", Location.WardType, hcm.Id, 1);
        var anLac = Location.Create("27460", "Phường An Lạc", Location.WardType, hcm.Id, 2);
        var handler = new ListLocationsHandler(TestLocationRepository.From(hcm, vungTau, anLac));

        var result = await handler.Handle(new ListLocationsQuery("79", "Vũng Tàu"), CancellationToken.None);

        var item = result.Should().ContainSingle().Which;
        item.Code.Should().Be("26506");
        item.Type.Should().Be(Location.WardType);
        item.ParentId.Should().Be(hcm.Id);
        item.ParentCode.Should().Be("79");
        item.ParentName.Should().Be("Thành phố Hồ Chí Minh");
    }

    [Theory]
    [InlineData(Location.CommuneType, "26734")]
    [InlineData(Location.SpecialZoneType, "26735")]
    public async Task ListLocations_TypeAndParentAndSearch_AreCombinedWithAnd(
        string type,
        string expectedCode)
    {
        var province = Location.Create("79", "Province", Location.MunicipalityType, 1);
        var commune = Location.Create("26734", "Coastal Area", Location.CommuneType, province.Id, 1);
        var specialZone = Location.Create("26735", "Coastal Area", Location.SpecialZoneType, province.Id, 2);
        var ward = Location.Create("26736", "Other Area", Location.WardType, province.Id, 3);
        var handler = new ListLocationsHandler(
            TestLocationRepository.From(province, commune, specialZone, ward));

        var result = await handler.Handle(
            new ListLocationsQuery("79", "Coastal", type),
            CancellationToken.None);

        result.Should().ContainSingle().Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task ListLocations_LeafTypeWithoutParent_ReturnsEmptyList()
    {
        var province = Location.Create("79", "Province", Location.MunicipalityType, 1);
        var ward = Location.Create("26736", "Ward", Location.WardType, province.Id, 1);
        var handler = new ListLocationsHandler(TestLocationRepository.From(province, ward));

        var result = await handler.Handle(
            new ListLocationsQuery(null, null, Location.WardType),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListLocations_InvalidType_ThrowsValidationExceptionOnType()
    {
        var handler = new ListLocationsHandler(TestLocationRepository.Create());

        var act = () => handler.Handle(
            new ListLocationsQuery(null, null, "DISTRICT"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "type");
    }

    [Fact]
    public async Task CreateLocation_LeafRequiresActiveTopLevelParent()
    {
        var hcm = Location.Create("79", "Thành phố Hồ Chí Minh", Location.MunicipalityType, 1);
        var placeholder = Location.Create("27460", "Phường An Lạc", Location.WardType, hcm.Id, 1);
        var repository = TestLocationRepository.From(hcm, placeholder);
        var handler = new CreateLocationHandler(repository, new FakeUnitOfWork());

        var result = await handler.Handle(
            new CreateLocationCommand("26506", "Phường Vũng Tàu", Location.WardType, 2, true, "79"),
            CancellationToken.None);

        result.ParentCode.Should().Be("79");
        result.Type.Should().Be(Location.WardType);
        repository.Query().Should().Contain(location => location.Code == "26506");
    }

    [Fact]
    public async Task CreateLocation_RootRejectsParentCode()
    {
        var hcm = Location.Create("79", "Thành phố Hồ Chí Minh", Location.MunicipalityType, 1);
        var leaf = Location.Create("27460", "Phường An Lạc", Location.WardType, hcm.Id, 1);
        var handler = new CreateLocationHandler(TestLocationRepository.From(hcm, leaf), new FakeUnitOfWork());

        var act = () => handler.Handle(
            new CreateLocationCommand("01", "Thành phố Hà Nội", Location.MunicipalityType, 2, true, "79"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "parentCode");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
            => action();

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
    }
}
