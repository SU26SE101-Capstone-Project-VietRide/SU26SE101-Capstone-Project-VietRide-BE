using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class GetVehicleHandler : IRequestHandler<GetVehicleQuery, VehicleDto>
{
    private readonly IVehicleRepository vehicleRepository;

    public GetVehicleHandler(IVehicleRepository vehicleRepository)
    {
        this.vehicleRepository = vehicleRepository;
    }

    public async Task<VehicleDto> Handle(GetVehicleQuery request, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.VehicleId,
            cancellationToken);

        return vehicle is null
            ? throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.")
            : VehicleMapper.ToDto(vehicle);
    }
}
