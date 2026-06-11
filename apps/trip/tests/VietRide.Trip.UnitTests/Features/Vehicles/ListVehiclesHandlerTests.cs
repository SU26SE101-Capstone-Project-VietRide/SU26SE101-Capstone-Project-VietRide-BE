using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class ListVehiclesHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsTenantFilterSearchSortAndPaging()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        object?[]? receivedArguments = null;
        var repository = TestProxy<IVehicleRepository>.Create((method, args) =>
        {
            if (method.Name != nameof(IVehicleRepository.ListByOperatorAsync))
                return null;

            receivedArguments = args;
            return PagedResult<VietRide.Trip.Domain.Entities.Vehicle>.Create([vehicle], 2, 5, 6);
        });
        var handler = new ListVehiclesHandler(repository);

        var result = await handler.Handle(
            new ListVehiclesQuery(operatorId, 2, 5, "51A", "licensePlate", "createdAt", "desc"),
            CancellationToken.None);

        Assert.NotNull(receivedArguments);
        Assert.Equal(operatorId, receivedArguments[0]);
        Assert.Equal("51A", receivedArguments[3]);
        Assert.Equal("licensePlate", receivedArguments[4]);
        Assert.Equal("createdAt", receivedArguments[5]);
        Assert.Equal("desc", receivedArguments[6]);
        Assert.Single(result.Items);
        Assert.Equal(6, result.TotalItems);
    }

    [Fact]
    public async Task Handle_WhenOperatorHasNoVehicles_ReturnsEmptyPage()
    {
        var repository = TestProxy<IVehicleRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleRepository.ListByOperatorAsync)
                ? PagedResult<VietRide.Trip.Domain.Entities.Vehicle>.Create([], 1, 20, 0)
                : null);
        var handler = new ListVehiclesHandler(repository);

        var result = await handler.Handle(
            new ListVehiclesQuery(Guid.NewGuid(), null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Handle_WhenSortDirIsOmitted_DefaultsToDescending()
    {
        object?[]? receivedArguments = null;
        var repository = TestProxy<IVehicleRepository>.Create((method, args) =>
        {
            if (method.Name != nameof(IVehicleRepository.ListByOperatorAsync))
                return null;

            receivedArguments = args;
            return PagedResult<VietRide.Trip.Domain.Entities.Vehicle>.Create([], 1, 20, 0);
        });

        await new ListVehiclesHandler(repository).Handle(
            new ListVehiclesQuery(Guid.NewGuid(), null, null, null, null, null, null),
            CancellationToken.None);

        Assert.NotNull(receivedArguments);
        Assert.Equal("desc", receivedArguments[6]);
    }

    [Fact]
    public async Task Handle_WithInvalidSortBy_ThrowsInvalidSortFieldBadRequest()
    {
        var handler = new ListVehiclesHandler(TestProxy<IVehicleRepository>.Create((_, _) => null));

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new ListVehiclesQuery(Guid.NewGuid(), 1, 20, null, null, "operatorId", null),
            CancellationToken.None));

        Assert.Equal("INVALID_SORT_FIELD", exception.ErrorCode);
    }
}
