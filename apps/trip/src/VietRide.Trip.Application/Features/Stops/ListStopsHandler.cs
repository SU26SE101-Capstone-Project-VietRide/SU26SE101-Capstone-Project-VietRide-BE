using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class ListStopsHandler : IRequestHandler<ListStopsQuery, PagedResult<StopDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ILocationRepository? locationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IRouteStopRepository? routeStopRepository;

    public ListStopsHandler(
        IStopRepository stopRepository,
        ILocationRepository? locationRepository = null,
        IRouteStopRepository? routeStopRepository = null)
    {
        this.stopRepository = stopRepository;
        this.locationRepository = locationRepository;
        this.routeStopRepository = routeStopRepository;
    }

    public Task<PagedResult<StopDto>> Handle(ListStopsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        if (page < 1 || pageSize < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.Search?.Trim().Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var query = (string.IsNullOrWhiteSpace(request.Search)
                ? stopRepository.QueryNoTracking()
                : stopRepository.SearchByTextNoTracking(request.Search))
            .Where(stop => stop.OperatorId == request.OperatorId && stop.DeletedAt == null);

        if (request.IsActive.HasValue)
            query = query.Where(stop => stop.IsActive == request.IsActive.Value);
        if (request.RouteId.HasValue)
        {
            var stopIds = routeStopRepository?.QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == request.RouteId.Value)
                .Select(routeStop => routeStop.StopId)
                ?? throw new InvalidOperationException("Route-stop repository is required for routeId filtering.");
            query = query.Where(stop => stopIds.Contains(stop.Id));
        }

        var totalItems = query.LongCount();
        var stops = query
            .OrderBy(stop => stop.Name)
            .ThenBy(stop => stop.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var locations = StopLocationContextResolver.Resolve(locationRepository, stops);
        var items = stops.Select(stop => StopMapper.ToDto(stop, locations)).ToList();

        return Task.FromResult(PagedResult<StopDto>.Create(items, page, pageSize, totalItems));
    }
}
