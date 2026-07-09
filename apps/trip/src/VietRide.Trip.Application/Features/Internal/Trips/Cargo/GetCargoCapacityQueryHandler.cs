using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed class GetCargoCapacityQueryHandler : IRequestHandler<GetCargoCapacityQuery, CargoCapacityDto>
{
    private readonly ITripRepository tripRepository;

    public GetCargoCapacityQueryHandler(ITripRepository tripRepository)
    {
        this.tripRepository = tripRepository;
    }

    public async Task<CargoCapacityDto> Handle(GetCargoCapacityQuery request, CancellationToken cancellationToken)
    {
        var trip = await tripRepository.QueryNoTracking()
            .FirstOrDefaultAsync(trip => trip.Id == request.TripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        }

        if (request.OperatorId.HasValue && trip.OperatorId != request.OperatorId.Value)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        }

        var maxWeight = trip.MaxCargoWeightKg ?? 0m;
        var maxVolume = trip.MaxCargoVolumeM3 ?? 0m;
        var percentFull = maxWeight <= 0m ? 0m : Math.Round(trip.TotalLoadedWeightKg / maxWeight * 100m, 2);
        return new CargoCapacityDto(
            trip.Id,
            trip.ReservedParcelWeightKg,
            trip.ReservedParcelVolumeM3,
            trip.TotalLoadedWeightKg,
            trip.TotalLoadedVolumeM3,
            maxWeight,
            maxVolume,
            Math.Max(0m, maxWeight - trip.ReservedParcelWeightKg - trip.TotalLoadedWeightKg),
            Math.Max(0m, maxVolume - trip.ReservedParcelVolumeM3 - trip.TotalLoadedVolumeM3),
            percentFull);
    }
}
