using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.VehicleTypes;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.VehicleTypes;

public sealed class ListVehicleTypesHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsSearchSortAndPaging()
    {
        var vehicleType = VehicleType.Create("STANDARD_BUS", "Standard bus", 20, 40, true);
        object?[]? receivedArguments = null;
        var repository = TestProxy<IVehicleTypeRepository>.Create((method, args) =>
        {
            if (method.Name != nameof(IVehicleTypeRepository.ListActiveAsync))
                return null;

            receivedArguments = args;
            return PagedResult<VehicleType>.Create([vehicleType], 1, 20, 1);
        });
        var handler = new ListVehicleTypesHandler(repository);

        var result = await handler.Handle(
            new ListVehicleTypesQuery(1, 20, "STANDARD", "code", "createdAt", "desc"),
            CancellationToken.None);

        Assert.NotNull(receivedArguments);
        Assert.Equal("STANDARD", receivedArguments[2]);
        Assert.Equal("code", receivedArguments[3]);
        Assert.Equal("createdAt", receivedArguments[4]);
        Assert.Equal("desc", receivedArguments[5]);
        Assert.Single(result.Items);
        Assert.Equal(vehicleType.CreatedAt, result.Items[0].CreatedAt);
        Assert.Equal(vehicleType.UpdatedAt, result.Items[0].UpdatedAt);
    }

    [Fact]
    public async Task Handle_WhenCatalogIsEmpty_ReturnsEmptyPage()
    {
        var repository = TestProxy<IVehicleTypeRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleTypeRepository.ListActiveAsync)
                ? PagedResult<VehicleType>.Create([], 1, 20, 0)
                : null);
        var handler = new ListVehicleTypesHandler(repository);

        var result = await handler.Handle(
            new ListVehicleTypesQuery(null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Handle_WhenSortDirIsOmitted_DefaultsToDescending()
    {
        object?[]? receivedArguments = null;
        var repository = TestProxy<IVehicleTypeRepository>.Create((method, args) =>
        {
            if (method.Name != nameof(IVehicleTypeRepository.ListActiveAsync))
                return null;

            receivedArguments = args;
            return PagedResult<VehicleType>.Create([], 1, 20, 0);
        });

        await new ListVehicleTypesHandler(repository).Handle(
            new ListVehicleTypesQuery(null, null, null, null, null, null),
            CancellationToken.None);

        Assert.NotNull(receivedArguments);
        Assert.Equal("desc", receivedArguments[5]);
    }

    [Fact]
    public async Task Handle_WithInvalidSortBy_ThrowsInvalidSortFieldBadRequest()
    {
        var handler = new ListVehicleTypesHandler(
            TestProxy<IVehicleTypeRepository>.Create((_, _) => null));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new ListVehicleTypesQuery(1, 20, null, null, "operatorId", null),
            CancellationToken.None));

        Assert.Equal("INVALID_SORT_FIELD", exception.ErrorCode);
    }
}
