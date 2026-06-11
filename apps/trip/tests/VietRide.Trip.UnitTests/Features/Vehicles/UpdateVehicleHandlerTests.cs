using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class UpdateVehicleHandlerTests
{
    [Fact]
    public async Task Handle_WithAllMutableFields_UpdatesVehicle()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        var newVehicleType = VehicleType.Create("LIMOUSINE", "Limousine", 20, 3, true);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) => method.Name switch
        {
            nameof(IVehicleRepository.GetOwnedByIdAsync) => vehicle,
            nameof(IVehicleRepository.LicensePlateExistsAsync) => false,
            nameof(IVehicleRepository.TryUpdateAsync) => true,
            _ => null,
        });
        var handler = new UpdateVehicleHandler(
            AllowedIdentityClient(),
            repository,
            VehicleTypeRepository(newVehicleType));

        var result = await handler.Handle(
            new UpdateVehicleCommand(
                operatorId,
                vehicle.Id,
                newVehicleType.Id,
                "51B-99999",
                VehicleTestData.CreateSeatLayout(3),
                true,
                3,
                null,
                true,
                20m,
                true,
                VehicleStatusDto.MAINTENANCE,
                false),
            CancellationToken.None);

        Assert.Equal(newVehicleType.Id, result.VehicleTypeId);
        Assert.Equal("51B-99999", result.LicensePlate);
        Assert.Equal(3, result.TotalSeats);
        Assert.Null(result.MaxCargoWeightKg);
        Assert.Equal(20m, result.MaxCargoVolumeM3);
        Assert.Equal(VehicleStatusDto.MAINTENANCE, result.Status);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Handle_WithOnlyTotalSeats_ValidatesAgainstStoredLayout()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleRepository.GetOwnedByIdAsync) ? vehicle : null);
        var handler = new UpdateVehicleHandler(
            AllowedIdentityClient(),
            repository,
            VehicleTypeRepository(null));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            EmptyCommand(operatorId, vehicle.Id) with { TotalSeats = 3 },
            CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "totalSeats");
    }

    [Fact]
    public async Task Handle_WhenVehicleIsOutsideOperatorScope_ThrowsExpectedCode()
    {
        var handler = new UpdateVehicleHandler(
            AllowedIdentityClient(),
            TestProxy<IVehicleRepository>.Create((_, _) => null),
            VehicleTypeRepository(null));

        var exception = await Assert.ThrowsAsync<CodedNotFoundException>(() => handler.Handle(
            EmptyCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal("VEHICLE_NOT_FOUND", exception.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenVehicleTypeIsInactive_ThrowsExpectedCode()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleRepository.GetOwnedByIdAsync) ? vehicle : null);
        var handler = new UpdateVehicleHandler(
            AllowedIdentityClient(),
            repository,
            VehicleTypeRepository(null));

        var exception = await Assert.ThrowsAsync<CodedNotFoundException>(() => handler.Handle(
            EmptyCommand(operatorId, vehicle.Id) with { VehicleTypeId = Guid.NewGuid() },
            CancellationToken.None));

        Assert.Equal("VEHICLE_TYPE_NOT_FOUND", exception.ErrorCode);
    }

    [Fact]
    public async Task Handle_WhenUniqueIndexRaceOccurs_ReturnsLicensePlateValidation()
    {
        var operatorId = Guid.NewGuid();
        var vehicle = VehicleTestData.CreateVehicle(operatorId);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) => method.Name switch
        {
            nameof(IVehicleRepository.GetOwnedByIdAsync) => vehicle,
            nameof(IVehicleRepository.LicensePlateExistsAsync) => false,
            nameof(IVehicleRepository.TryUpdateAsync) => false,
            _ => null,
        });
        var handler = new UpdateVehicleHandler(
            AllowedIdentityClient(),
            repository,
            VehicleTypeRepository(null));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            EmptyCommand(operatorId, vehicle.Id) with { LicensePlate = "51B-99999" },
            CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "licensePlate");
    }

    private static UpdateVehicleCommand EmptyCommand(Guid operatorId, Guid vehicleId)
        => new(
            operatorId,
            vehicleId,
            null,
            null,
            null,
            false,
            null,
            null,
            false,
            null,
            false,
            null,
            null);

    private static IIdentityInternalClient AllowedIdentityClient()
        => TestProxy<IIdentityInternalClient>.Create((_, _) =>
            OperatorWriteEligibilityValidation.Allowed());

    private static IVehicleTypeRepository VehicleTypeRepository(VehicleType? vehicleType)
        => TestProxy<IVehicleTypeRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleTypeRepository.GetActiveByIdAsync) ? vehicleType : null);
}
