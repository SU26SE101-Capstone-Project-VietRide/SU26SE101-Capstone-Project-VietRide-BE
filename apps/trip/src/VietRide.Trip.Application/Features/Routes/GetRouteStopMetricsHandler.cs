using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class GetRouteStopMetricsHandler
    : IRequestHandler<GetRouteStopMetricsQuery, IReadOnlyList<RouteStopMetricDto>>
{
    private readonly IRouteRepository routes;
    private readonly IRouteStopRepository routeStops;
    private readonly IStopRepository stops;

    public GetRouteStopMetricsHandler(
        IRouteRepository routes,
        IRouteStopRepository routeStops,
        IStopRepository stops)
    {
        this.routes = routes;
        this.routeStops = routeStops;
        this.stops = stops;
    }

    public async Task<IReadOnlyList<RouteStopMetricDto>> Handle(
        GetRouteStopMetricsQuery request,
        CancellationToken cancellationToken)
    {
        _ = await routes.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        var items = await routeStops.ListByRouteAsync(request.RouteId, cancellationToken);
        var stopIds = items.Select(item => item.StopId).ToArray();
        var names = stops.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionary(stop => stop.Id, stop => stop.Name);
        return items.Select(item => new RouteStopMetricDto(
            item.StopId,
            names.TryGetValue(item.StopId, out var name) ? name : string.Empty,
            item.OrderIndex,
            item.DistanceFromOriginKm,
            item.EstimatedDurationFromOriginMinutes)).ToArray();
    }
}
