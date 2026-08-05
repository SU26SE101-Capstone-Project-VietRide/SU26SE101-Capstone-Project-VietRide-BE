using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetShuttleRoadDistance;

public sealed class GetShuttleRoadDistanceHandler
    : IRequestHandler<GetShuttleRoadDistanceQuery, ShuttleRoadDistanceDto>
{
    private readonly ITripRepository _trips;
    private readonly IRouteRepository _routes;
    private readonly IStationRepository _stations;
    private readonly IShuttleDistanceClient _distanceClient;

    public GetShuttleRoadDistanceHandler(
        ITripRepository trips,
        IRouteRepository routes,
        IStationRepository stations,
        IShuttleDistanceClient distanceClient)
    {
        _trips = trips;
        _routes = routes;
        _stations = stations;
        _distanceClient = distanceClient;
    }

    public async Task<ShuttleRoadDistanceDto> Handle(
        GetShuttleRoadDistanceQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Direction is not (ShuttleTrip.InboundDirection or ShuttleTrip.OutboundDirection))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle direction is invalid.");
        }

        if (request.Latitude is < -90m or > 90m || request.Longitude is < -180m or > 180m)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle coordinates are invalid.");
        }

        var trip = _trips.QueryNoTracking().FirstOrDefault(x => x.Id == request.TripId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var route = _routes.QueryNoTracking().FirstOrDefault(x => x.Id == trip.RouteId)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route was not found.");
        var stationId = request.Direction == ShuttleTrip.InboundDirection
            ? route.OriginStationId
            : route.DestinationStationId;
        var station = _stations.QueryNoTracking().FirstOrDefault(x => x.Id == stationId)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Shuttle station was not found.");

        if (!station.IsActive || station.DeletedAt.HasValue || !station.SupportsShuttle
            || !station.Latitude.HasValue || !station.Longitude.HasValue)
        {
            throw new CodedValidationException(
                "SHUTTLE_STATION_NOT_SUPPORTED",
                "The direction-specific Station does not support shuttle service.");
        }

        var outcome = await _distanceClient.CalculateAsync(
            station.Latitude.Value,
            station.Longitude.Value,
            request.Latitude,
            request.Longitude,
            cancellationToken);
        return outcome switch
        {
            ShuttleDistanceOutcome.Success success => new ShuttleRoadDistanceDto(success.DistanceMeters),
            ShuttleDistanceOutcome.Unavailable unavailable => throw new ShuttleDistanceUnavailableException(unavailable.Message),
            _ => throw new ShuttleDistanceUnavailableException("Google Routes returned an unknown response."),
        };
    }
}

public sealed class ShuttleDistanceUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 503;
    public string ErrorCode => "SHUTTLE_DISTANCE_UNAVAILABLE";

    public ShuttleDistanceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
