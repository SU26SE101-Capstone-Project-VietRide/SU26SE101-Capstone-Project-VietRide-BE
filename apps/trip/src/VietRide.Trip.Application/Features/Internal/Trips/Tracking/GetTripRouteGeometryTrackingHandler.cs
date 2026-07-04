using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class GetTripRouteGeometryTrackingHandler
    : IRequestHandler<GetTripRouteGeometryTrackingQuery, TripRouteGeometryTrackingResponse>
{
    private readonly IMediator mediator;

    public GetTripRouteGeometryTrackingHandler(IMediator mediator)
    {
        this.mediator = mediator;
    }

    public async Task<TripRouteGeometryTrackingResponse> Handle(
        GetTripRouteGeometryTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var routeStops = await mediator.Send(
            new GetTripRouteStopsTrackingQuery(request.TripId),
            cancellationToken);
        var points = routeStops.Stops
            .OrderBy(stop => stop.Sequence)
            .Select(stop => new RouteGeometryPointDto(stop.Latitude, stop.Longitude))
            .ToArray();

        return new TripRouteGeometryTrackingResponse(request.TripId, points, null);
    }
}
