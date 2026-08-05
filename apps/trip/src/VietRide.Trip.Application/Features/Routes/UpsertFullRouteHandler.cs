using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Common.Geometry;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class UpsertFullRouteHandler : IRequestHandler<UpsertFullRouteCommand, RouteDto>
{
    private readonly IIdentityInternalClient identityClient;
    private readonly IOperatorStationRepository operatorStations;
    private readonly IRouteRepository routes;
    private readonly IRouteStopRepository routeStops;
    private readonly IStationRepository stations;
    private readonly IStopRepository stops;
    private readonly IUnitOfWork unitOfWork;

    public UpsertFullRouteHandler(
        IIdentityInternalClient identityClient,
        IOperatorStationRepository operatorStations,
        IRouteRepository routes,
        IRouteStopRepository routeStops,
        IStationRepository stations,
        IStopRepository stops,
        IUnitOfWork unitOfWork)
    {
        this.identityClient = identityClient;
        this.operatorStations = operatorStations;
        this.routes = routes;
        this.routeStops = routeStops;
        this.stations = stations;
        this.stops = stops;
        this.unitOfWork = unitOfWork;
    }

    public async Task<RouteDto> Handle(UpsertFullRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identityClient, request.OperatorId, cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            identityClient, request.OperatorId, requireShuttleModule: false, cancellationToken);

        var quotaClient = identityClient as ISubscriptionQuotaClient;
        QuotaAllocationResult? quota = null;
        var completed = false;
        try
        {
            ValidateStopCollection(request);
            var stationById = ValidateStations(request);
            var stopById = ValidateStops(request);
            await ValidateReturnRouteAsync(request, cancellationToken);

            var isNew = !request.RouteId.HasValue;
            Route route;
            if (!isNew)
            {
                var routeId = request.RouteId
                    ?? throw new InvalidOperationException("Full Route update requires a Route id.");
                route = await routes.GetOwnedByIdAsync(request.OperatorId, routeId, cancellationToken)
                    ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
                if (route.OriginStationId != request.OriginStationId
                    || route.DestinationStationId != request.DestinationStationId)
                {
                    throw new CodedValidationException(
                        "ROUTE_STATION_IMMUTABLE",
                        "A full Route update cannot change origin or destination.",
                        [new ValidationError("originStationId", "Create a new Route to change the station pair.")]);
                }
            }
            else
            {
                route = Route.Create(
                    request.OperatorId,
                    request.Name!,
                    request.OriginStationId,
                    request.DestinationStationId,
                    Money.FromRaw(request.BaseFare),
                    null,
                    null,
                    request.ReturnRouteId);
            }

            var duplicate = await routes.FindDuplicateWithTransactionLockAsync(
                request.OperatorId,
                request.Name!,
                request.OriginStationId,
                request.DestinationStationId,
                request.RouteId,
                cancellationToken);
            if (duplicate is not null)
            {
                throw new CodedConflictException(
                    "ROUTE_DUPLICATED",
                    "A Route with the same normalized name and station pair already exists.",
                    [new ValidationError("existingRouteId", duplicate.Id.ToString("D"))]);
            }

            if (isNew)
            {
                quota = quotaClient is null ? null : await quotaClient.ClaimQuotaAllocationAsync(
                    request.OperatorId,
                    "ROUTES",
                    route.Id,
                    periodKey: null,
                    cancellationToken);
                if (quota is not null && !quota.IsAllowed)
                {
                    throw new CodedValidationException(
                        quota.ErrorCode ?? "SUBSCRIPTION_LIMIT_EXCEEDED",
                        quota.Message ?? "Subscription route limit exceeded.");
                }
            }

            IReadOnlyList<GeoPoint>? polyline = null;
            if (request.PathPolyline is not null)
            {
                polyline = RouteGeometryValidator.DecodeAndValidate(request.PathPolyline);
                RouteGeometryValidator.ValidateWaypoints(
                    polyline,
                    stopById.Values.Select(stop => (stop.Id, new GeoPoint((double)stop.Latitude, (double)stop.Longitude))),
                    stationById.Values
                        .Where(station => station.Latitude.HasValue && station.Longitude.HasValue)
                        .Select(station => (station.Id, new GeoPoint((double)station.Latitude!.Value, (double)station.Longitude!.Value))));
            }

            (decimal DistanceKm, int DurationMinutes)? metrics = polyline is null
                ? null
                : RouteMetricsCalculator.Calculate(polyline);
            var distanceKm = metrics?.DistanceKm ?? request.ManualDistanceKm ?? route.TotalDistanceKm;
            var durationMinutes = metrics?.DurationMinutes ?? request.ManualDurationMinutes ?? route.EstimatedDurationMinutes;
            route.UpdateDetails(
                request.Name!,
                route.OriginStationId,
                route.DestinationStationId,
                Money.FromRaw(request.BaseFare),
                distanceKm,
                durationMinutes,
                request.ReturnRouteId);
            route.SetPathGeometry(request.PathPolyline);
            if (request.IsActive == false)
                route.Deactivate();
            else if (request.IsActive == true)
                route.Activate();
            if (isNew)
                await routes.AddAsync(route, cancellationToken);
            else
                routes.Update(route);

            var existingRouteStops = routeStops.Query().Where(item => item.RouteId == route.Id).ToArray();
            foreach (var existing in existingRouteStops)
                routeStops.Remove(existing);
            if (existingRouteStops.Length > 0)
                await unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var input in request.Stops.OrderBy(item => item.OrderIndex))
            {
                var projected = polyline is null
                    ? ((decimal DistanceKm, int DurationMinutes)?)null
                    : RouteMetricsCalculator.Project(
                        new GeoPoint((double)stopById[input.StopId].Latitude, (double)stopById[input.StopId].Longitude),
                        polyline);
                var stopDistanceKm = input.DistanceFromOriginKm ?? projected?.DistanceKm;
                var stopDurationMinutes = input.EstimatedDurationFromOriginMinutes ?? projected?.DurationMinutes;
                if (!stopDurationMinutes.HasValue)
                {
                    throw new CodedValidationException(
                        "ROUTE_STOP_ORDER_INVALID",
                        "Stop metrics are required when Route geometry is absent.",
                        [new ValidationError("stops", "Each stop needs duration metrics without a polyline.")]);
                }

                await routeStops.AddAsync(RouteStop.Create(
                    route.Id,
                    input.StopId,
                    input.OrderIndex,
                    stopDurationMinutes.Value,
                    stopDistanceKm,
                    input.AllowPickup,
                    input.AllowDropoff), cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            var response = RouteDetailsProjector.Project(route, stations, routeStops, stops);
            completed = true;
            return response;
        }
        finally
        {
            if (!completed
                && quotaClient is not null
                && quota?.AllocationId is { } allocationId
                && allocationId != Guid.Empty)
            {
                await quotaClient.ReleaseQuotaAllocationAsync(request.OperatorId, allocationId, cancellationToken);
            }
        }
    }

    private Dictionary<Guid, Station> ValidateStations(UpsertFullRouteCommand request)
    {
        var stationIds = new[] { request.OriginStationId, request.DestinationStationId };
        var stationById = stations.QueryNoTracking()
            .Where(station => stationIds.Contains(station.Id) && station.IsActive && station.DeletedAt == null)
            .ToDictionary(station => station.Id);
        var linkedIds = operatorStations.QueryNoTracking()
            .Where(link => link.OperatorId == request.OperatorId && link.IsActive && stationIds.Contains(link.StationId))
            .Select(link => link.StationId)
            .ToHashSet();
        if (stationById.Count != 2 || linkedIds.Count != 2)
        {
            throw new CodedValidationException(
                "ROUTE_STATION_INVALID",
                "Both Route stations must exist and be actively linked to the operator.");
        }

        return stationById;
    }

    private Dictionary<Guid, Stop> ValidateStops(UpsertFullRouteCommand request)
    {
        var stopIds = request.Stops.Select(item => item.StopId).ToArray();
        var stopById = stops.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id)
                && stop.OperatorId == request.OperatorId
                && stop.IsActive
                && stop.DeletedAt == null)
            .ToDictionary(stop => stop.Id);
        if (stopById.Count != stopIds.Length)
            throw new CodedValidationException("ROUTE_STATION_INVALID", "One or more Route stops are invalid.");
        return stopById;
    }

    private static void ValidateStopCollection(UpsertFullRouteCommand request)
    {
        if (request.Stops.Select(item => item.StopId).Distinct().Count() != request.Stops.Count)
            throw new CodedValidationException("ROUTE_STOP_DUPLICATED", "A stop can appear only once in a Route.");

        var expectedOrder = Enumerable.Range(1, request.Stops.Count);
        if (!request.Stops.Select(item => item.OrderIndex).Order().SequenceEqual(expectedOrder))
            throw new CodedValidationException("ROUTE_STOP_ORDER_INVALID", "Route stop order must be unique and contiguous from 1.");

        if (request.Stops.Any(item => !item.AllowPickup && !item.AllowDropoff))
            throw new CodedValidationException("ROUTE_STOP_ORDER_INVALID", "Each Route stop must allow pickup or dropoff.");
    }

    private async Task ValidateReturnRouteAsync(UpsertFullRouteCommand request, CancellationToken cancellationToken)
    {
        if (request.ReturnRouteId.HasValue
            && !await routes.ExistsActiveOwnedByOperatorAsync(request.OperatorId, request.ReturnRouteId.Value, cancellationToken))
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Return Route was not found.");
        }
    }
}
