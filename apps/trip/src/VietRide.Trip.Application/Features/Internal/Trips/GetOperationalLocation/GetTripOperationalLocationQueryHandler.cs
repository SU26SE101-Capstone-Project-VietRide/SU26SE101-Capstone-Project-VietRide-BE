using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetOperationalLocation;

public sealed class GetTripOperationalLocationQueryHandler
    : IRequestHandler<GetTripOperationalLocationQuery, TripOperationalLocationDto>
{
    private readonly ITripRepository _trips;
    private readonly ITripStopRepository _tripStops;

    public GetTripOperationalLocationQueryHandler(
        ITripRepository trips,
        ITripStopRepository tripStops)
    {
        _trips = trips;
        _tripStops = tripStops;
    }

    public Task<TripOperationalLocationDto> Handle(
        GetTripOperationalLocationQuery request,
        CancellationToken cancellationToken)
    {
        var trip = _trips.QueryNoTracking().FirstOrDefault(x => x.Id == request.TripId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var currentStop = _tripStops.QueryNoTracking()
            .Where(x => x.TripId == request.TripId
                && x.Status == TripStopStatus.ARRIVED
                && x.ActualDepartureTime == null)
            .OrderByDescending(x => x.OrderIndex)
            .FirstOrDefault();

        return Task.FromResult(new TripOperationalLocationDto(
            trip.Id,
            trip.VehicleId,
            trip.Status.ToString(),
            currentStop?.StopId,
            currentStop?.Status.ToString(),
            currentStop?.ActualArrivalTime,
            currentStop?.ActualDepartureTime,
            trip.DestinationArrivedAt));
    }
}
