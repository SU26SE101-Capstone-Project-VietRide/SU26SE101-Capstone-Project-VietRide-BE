using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class GetTripRouteStopsTrackingHandler
    : IRequestHandler<GetTripRouteStopsTrackingQuery, TripRouteStopsTrackingResponse>
{
    private readonly IStopRepository stopRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripStopRepository tripStopRepository;

    public GetTripRouteStopsTrackingHandler(
        ITripRepository tripRepository,
        ITripStopRepository tripStopRepository,
        IStopRepository stopRepository)
    {
        this.tripRepository = tripRepository;
        this.tripStopRepository = tripStopRepository;
        this.stopRepository = stopRepository;
    }

    public async Task<TripRouteStopsTrackingResponse> Handle(
        GetTripRouteStopsTrackingQuery request,
        CancellationToken cancellationToken)
    {
        _ = await tripRepository.QueryNoTracking()
            .FirstOrDefaultAsync(trip => trip.Id == request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        var tripStops = await tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == request.TripId)
            .OrderBy(stop => stop.OrderIndex)
            .ToArrayAsync(cancellationToken);

        var stopIds = tripStops.Select(stop => stop.StopId).ToArray();
        var stopsById = await stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionaryAsync(stop => stop.Id, cancellationToken);

        var stops = tripStops
            .Select(stop =>
            {
                if (!stopsById.TryGetValue(stop.StopId, out var stopSnapshot))
                {
                    throw new CodedNotFoundException("STOP_NOT_FOUND", "Trip stop snapshot was not found.");
                }

                return new TripRouteStopTrackingDto(
                    stop.StopId,
                    (double)stopSnapshot.Latitude,
                    (double)stopSnapshot.Longitude,
                    stop.OrderIndex,
                    null,
                    stop.EstimatedArrivalTime,
                    stop.Status.ToString());
            })
            .ToArray();

        return new TripRouteStopsTrackingResponse(stops);
    }
}
