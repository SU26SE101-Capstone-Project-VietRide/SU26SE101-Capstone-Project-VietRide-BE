using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips;

namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed class GetTripSeatMapHandler : IRequestHandler<GetTripSeatMapQuery, TripSeatMapDto>
{
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IVehicleTypeRepository vehicleTypeRepository;

    public GetTripSeatMapHandler(
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        IVehicleRepository vehicleRepository,
        IVehicleTypeRepository vehicleTypeRepository)
    {
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.vehicleRepository = vehicleRepository;
        this.vehicleTypeRepository = vehicleTypeRepository;
    }

    public async Task<TripSeatMapDto> Handle(GetTripSeatMapQuery request, CancellationToken cancellationToken)
    {
        var trip = tripRepository.QueryNoTracking().FirstOrDefault(trip => trip.Id == request.TripId)
            ?? throw TripNotFound();
        var vehicle = vehicleRepository.QueryNoTracking().FirstOrDefault(vehicle => vehicle.Id == trip.VehicleId)
            ?? throw TripNotFound();
        var vehicleType = await vehicleTypeRepository.GetByIdAsync(vehicle.VehicleTypeId, cancellationToken);
        var seats = tripSeatRepository.QueryNoTracking()
            .Where(seat => seat.TripId == trip.Id)
            .ToArray();

        return TripProjectionMapper.ToTripSeatMapDto(trip, vehicle, vehicleType, seats);
    }

    private static CodedNotFoundException TripNotFound() =>
        new("TRIP_NOT_FOUND", "Trip was not found.");
}
