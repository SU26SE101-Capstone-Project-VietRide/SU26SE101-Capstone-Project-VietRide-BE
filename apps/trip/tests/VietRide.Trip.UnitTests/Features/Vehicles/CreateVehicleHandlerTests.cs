using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

public sealed class CreateVehicleHandlerTests
{
    [Fact]
    public async Task Handle_WithValidInput_CreatesVehicle()
    {
        var operatorId = Guid.NewGuid();
        var vehicleType = VehicleType.Create("STANDARD_BUS", "Standard bus", 20, 2, true);
        Vehicle? addedVehicle = null;
        var identityClient = AllowedIdentityClient();
        var vehicleRepository = TestProxy<IVehicleRepository>.Create((method, args) =>
        {
            if (method.Name == nameof(IVehicleRepository.LicensePlateExistsAsync))
                return false;

            if (method.Name == nameof(IVehicleRepository.TryAddAsync))
            {
                addedVehicle = Assert.IsType<Vehicle>(args![0]);
                return true;
            }

            return null;
        });
        var vehicleTypeRepository = VehicleTypeRepository(vehicleType);
        var handler = new CreateVehicleHandler(identityClient, vehicleRepository, vehicleTypeRepository);

        var result = await handler.Handle(
            new CreateVehicleCommand(
                operatorId,
                vehicleType.Id,
                "51A-12345",
                VehicleTestData.CreateSeatLayout(),
                2,
                1000m,
                10m),
            CancellationToken.None);

        Assert.NotNull(addedVehicle);
        Assert.Equal(operatorId, result.OperatorId);
        Assert.Equal(vehicleType.Id, result.VehicleTypeId);
        Assert.Equal(2, result.SeatLayoutJson.Seats.Count);
    }

    [Fact]
    public async Task Handle_WhenShuttleModuleIsDisabled_UsesGeneralSubscriptionGuard()
    {
        var operatorId = Guid.NewGuid();
        var vehicleType = VehicleType.Create("STANDARD_BUS", "Standard bus", 20, 2, true);
        bool? requireShuttleModule = null;
        var identityClient = TestProxy<IIdentityInternalClient>.Create((method, args) =>
        {
            if (method.Name == nameof(IIdentityInternalClient.ValidateOperatorSubscriptionCanWriteAsync))
            {
                requireShuttleModule = Assert.IsType<bool>(args![1]);
                return requireShuttleModule.Value
                    ? new OperatorWriteEligibilityValidation(
                        false,
                        403,
                        "SUBSCRIPTION_MODULE_DISABLED",
                        "Shuttle module is disabled.")
                    : OperatorWriteEligibilityValidation.Allowed();
            }

            return OperatorWriteEligibilityValidation.Allowed();
        });
        var vehicleRepository = TestProxy<IVehicleRepository>.Create((method, _) => method.Name switch
        {
            nameof(IVehicleRepository.LicensePlateExistsAsync) => false,
            nameof(IVehicleRepository.TryAddAsync) => true,
            _ => null,
        });
        var handler = new CreateVehicleHandler(
            identityClient,
            vehicleRepository,
            VehicleTypeRepository(vehicleType));

        var result = await handler.Handle(
            new CreateVehicleCommand(
                operatorId,
                vehicleType.Id,
                "51A-12345",
                VehicleTestData.CreateSeatLayout(),
                2,
                null,
                null),
            CancellationToken.None);

        Assert.Equal(operatorId, result.OperatorId);
        Assert.Equal(false, requireShuttleModule);
    }

    [Fact]
    public async Task Handle_WhenVehicleTypeIsMissing_ThrowsExpectedCode()
    {
        var handler = new CreateVehicleHandler(
            AllowedIdentityClient(),
            TestProxy<IVehicleRepository>.Create((_, _) => null),
            VehicleTypeRepository(null));

        var exception = await Assert.ThrowsAsync<CodedNotFoundException>(() => handler.Handle(
            new CreateVehicleCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "51A-12345",
                VehicleTestData.CreateSeatLayout(),
                2,
                null,
                null),
            CancellationToken.None));

        Assert.Equal("VEHICLE_TYPE_NOT_FOUND", exception.ErrorCode);
    }

    [Fact]
    public async Task Handle_WithDuplicateSeatNumber_UsesContractValidationField()
    {
        var vehicleType = VehicleType.Create("STANDARD_BUS", "Standard bus", 20, 2, true);
        var duplicateSeat = new SeatLayoutSeatDto("A01", 1, 1, 1, "STANDARD", false, false, false);
        var invalidLayout = new SeatLayoutDto(
            1,
            "STANDARD_BUS",
            2,
            1,
            2,
            1,
            [],
            [duplicateSeat, duplicateSeat]);
        var handler = new CreateVehicleHandler(
            AllowedIdentityClient(),
            TestProxy<IVehicleRepository>.Create((_, _) => null),
            VehicleTypeRepository(vehicleType));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateVehicleCommand(
                Guid.NewGuid(),
                vehicleType.Id,
                "51A-12345",
                invalidLayout,
                2,
                null,
                null),
            CancellationToken.None));

        Assert.Contains(
            exception.Errors,
            error => error.Field == "seatLayoutJson.seats[].seatNumber");
    }

    [Fact]
    public async Task Handle_WhenUniqueIndexRaceOccurs_ReturnsLicensePlateValidation()
    {
        var vehicleType = VehicleType.Create("STANDARD_BUS", "Standard bus", 20, 2, true);
        var repository = TestProxy<IVehicleRepository>.Create((method, _) => method.Name switch
        {
            nameof(IVehicleRepository.LicensePlateExistsAsync) => false,
            nameof(IVehicleRepository.TryAddAsync) => false,
            _ => null,
        });
        var handler = new CreateVehicleHandler(
            AllowedIdentityClient(),
            repository,
            VehicleTypeRepository(vehicleType));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateVehicleCommand(
                Guid.NewGuid(),
                vehicleType.Id,
                "51A-12345",
                VehicleTestData.CreateSeatLayout(),
                2,
                null,
                null),
            CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "licensePlate");
    }

    private static IIdentityInternalClient AllowedIdentityClient()
        => TestProxy<IIdentityInternalClient>.Create((_, _) =>
            OperatorWriteEligibilityValidation.Allowed());

    private static IVehicleTypeRepository VehicleTypeRepository(VehicleType? vehicleType)
        => TestProxy<IVehicleTypeRepository>.Create((method, _) =>
            method.Name == nameof(IVehicleTypeRepository.GetActiveByIdAsync) ? vehicleType : null);
}
