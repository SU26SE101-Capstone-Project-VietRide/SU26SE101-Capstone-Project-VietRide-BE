using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class GetVehicleHandler : IRequestHandler<GetVehicleQuery, VehicleDto>
{
    private readonly IVehicleRepository vehicleRepository;
    private readonly IResourceAvailabilityService? resourceAvailability;
    private readonly IClock? clock;

    public GetVehicleHandler(
        IVehicleRepository vehicleRepository,
        IResourceAvailabilityService? resourceAvailability = null,
        IClock? clock = null)
    {
        this.vehicleRepository = vehicleRepository;
        this.resourceAvailability = resourceAvailability;
        this.clock = clock;
    }

    public async Task<VehicleDto> Handle(GetVehicleQuery request, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.VehicleId,
            cancellationToken);

        if (vehicle is null)
        {
            throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        }

        var assignment = resourceAvailability is null
            ? default
            : (await resourceAvailability.GetVehicleAssignmentsAsync(
                request.OperatorId,
                [vehicle.Id],
                clock?.UtcNow ?? DateTimeOffset.UtcNow,
                cancellationToken)).GetValueOrDefault(vehicle.Id);
        return VehicleMapper.ToDto(vehicle, assignment.Current, assignment.Next);
    }
}
