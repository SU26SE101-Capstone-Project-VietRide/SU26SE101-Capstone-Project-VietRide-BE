using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class UpdateVehicleHandler : IRequestHandler<UpdateVehicleCommand, VehicleDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IVehicleTypeRepository vehicleTypeRepository;

    public UpdateVehicleHandler(
        IIdentityInternalClient identityInternalClient,
        IVehicleRepository vehicleRepository,
        IVehicleTypeRepository vehicleTypeRepository)
    {
        this.identityInternalClient = identityInternalClient;
        this.vehicleRepository = vehicleRepository;
        this.vehicleTypeRepository = vehicleTypeRepository;
    }

    public async Task<VehicleDto> Handle(UpdateVehicleCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var vehicle = await vehicleRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.VehicleId,
            cancellationToken);
        if (vehicle is null)
        {
            throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        }

        if (request.VehicleTypeId.HasValue)
        {
            var vehicleType = await vehicleTypeRepository.GetActiveByIdAsync(
                request.VehicleTypeId.Value,
                cancellationToken);
            if (vehicleType is null)
                throw new CodedNotFoundException("VEHICLE_TYPE_NOT_FOUND", "Vehicle type was not found.");

            vehicle.ChangeVehicleType(vehicleType.Id);
        }

        if (request.LicensePlate is not null)
        {
            var licensePlate = request.LicensePlate.Trim();
            if (await vehicleRepository.LicensePlateExistsAsync(licensePlate, vehicle.Id, cancellationToken))
                throw DuplicateLicensePlate();

            vehicle.ChangeLicensePlate(licensePlate);
        }

        if (request.HasSeatLayoutJson || request.TotalSeats.HasValue)
        {
            var effectiveSeatLayout = request.HasSeatLayoutJson
                ? request.SeatLayoutJson
                : vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>();
            var effectiveTotalSeats = request.TotalSeats ?? vehicle.TotalSeats;
            SeatLayoutValidator.Validate(effectiveSeatLayout, effectiveTotalSeats);
            vehicle.UpdateSeatLayout(
                JsonSerializer.SerializeToElement(effectiveSeatLayout),
                effectiveTotalSeats);
        }

        if (request.HasMaxCargoWeightKg || request.HasMaxCargoVolumeM3)
            vehicle.UpdateCargoCapacity(
                request.HasMaxCargoWeightKg ? request.MaxCargoWeightKg : vehicle.MaxCargoWeightKg,
                request.HasMaxCargoVolumeM3 ? request.MaxCargoVolumeM3 : vehicle.MaxCargoVolumeM3);

        if (request.HasImageUrls)
            vehicle.UpdateImageUrls(request.ImageUrls);

        if (request.Status.HasValue)
        {
            vehicle.ChangeStatus((VehicleStatus)request.Status.Value);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                vehicle.Activate();
            }
            else
            {
                vehicle.Deactivate();
            }
        }

        if (!await vehicleRepository.TryUpdateAsync(vehicle, cancellationToken))
            throw DuplicateLicensePlate();

        return VehicleMapper.ToDto(vehicle);
    }

    private static ValidationException DuplicateLicensePlate()
        => new(
            "Vehicle license plate is already in use.",
            [new ValidationError("licensePlate", "A non-deleted vehicle already uses this license plate.")]);
}
