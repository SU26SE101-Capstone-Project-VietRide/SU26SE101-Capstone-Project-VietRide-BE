using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class GetVehicleHandlerTests
{
    [Fact]
    public async Task Handle_WhenVehicleIsOwned_ReturnsVehicle()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleRepository.GetOwnedByIdAsync) ? vehicle : null);
        var handler = new GetVehicleHandler(repository);

        var result = await handler.Handle(
            new GetVehicleQuery(operatorId, vehicle.Id),
            CancellationToken.None);

        Assert.Equal(vehicle.Id, result.Id);
        Assert.Equal(operatorId, result.OperatorId);
    }

    [Fact]
    public async Task Handle_WhenVehicleIsOutsideOperatorScope_ThrowsExpectedCode()
    {
        var repository = TestProxy<IVehicleRepository>.Create((_, _) => null);
        var handler = new GetVehicleHandler(repository);

        var exception = await Assert.ThrowsAsync<CodedNotFoundException>(() => handler.Handle(
            new GetVehicleQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal("VEHICLE_NOT_FOUND", exception.ErrorCode);
    }
}
