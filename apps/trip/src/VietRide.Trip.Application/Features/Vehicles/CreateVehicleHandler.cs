using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class CreateVehicleHandler : IRequestHandler<CreateVehicleCommand, VehicleDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IVehicleTypeRepository vehicleTypeRepository;

    public CreateVehicleHandler(
        IIdentityInternalClient identityInternalClient,
        IVehicleRepository vehicleRepository,
        IVehicleTypeRepository vehicleTypeRepository)
    {
        this.identityInternalClient = identityInternalClient;
        this.vehicleRepository = vehicleRepository;
        this.vehicleTypeRepository = vehicleTypeRepository;
    }

    public async Task<VehicleDto> Handle(CreateVehicleCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            requireShuttleModule: false,
            cancellationToken);

        var vehicleType = await vehicleTypeRepository.GetActiveByIdAsync(
            request.VehicleTypeId,
            cancellationToken);
        if (vehicleType is null)
            throw new CodedNotFoundException("VEHICLE_TYPE_NOT_FOUND", "Vehicle type was not found.");

        SeatLayoutValidator.Validate(request.SeatLayoutJson, request.TotalSeats);

        var licensePlate = request.LicensePlate!.Trim();
        if (await vehicleRepository.LicensePlateExistsAsync(licensePlate, null, cancellationToken))
        {
            throw DuplicateLicensePlate();
        }

        var vehicle = Vehicle.Create(
            request.OperatorId,
            vehicleType.Id,
            licensePlate,
            JsonSerializer.SerializeToElement(request.SeatLayoutJson),
            request.TotalSeats,
            request.MaxCargoWeightKg,
            request.MaxCargoVolumeM3,
            request.ImageUrls);

        var quotaClient = identityInternalClient as ISubscriptionQuotaClient;
        var quota = quotaClient is null ? null : await quotaClient.ClaimQuotaAllocationAsync(
            request.OperatorId,
            "VEHICLES",
            vehicle.Id,
            periodKey: null,
            cancellationToken);
        if (quota is not null && !quota.IsAllowed)
        {
            throw new CodedValidationException(
                quota.ErrorCode ?? "SUBSCRIPTION_LIMIT_EXCEEDED",
                quota.Message ?? "Subscription vehicle limit exceeded.");
        }

        if (!await vehicleRepository.TryAddAsync(vehicle, cancellationToken))
        {
            if (quota?.AllocationId.HasValue == true && quota.AllocationId.Value != Guid.Empty)
                await quotaClient!.ReleaseQuotaAllocationAsync(request.OperatorId, quota.AllocationId.Value, cancellationToken);
            throw DuplicateLicensePlate();
        }

        return VehicleMapper.ToDto(vehicle);
    }

    private static ValidationException DuplicateLicensePlate()
        => new(
            "Vehicle license plate is already in use.",
            [new ValidationError("licensePlate", "A non-deleted vehicle already uses this license plate.")]);
}
