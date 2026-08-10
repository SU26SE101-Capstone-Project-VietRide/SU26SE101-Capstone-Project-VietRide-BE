using MediatR;
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

    public ListStopsHandler(IStopRepository stopRepository, ILocationRepository? locationRepository = null)
    {
        this.stopRepository = stopRepository;
        this.locationRepository = locationRepository;
    }

    public Task<PagedResult<StopDto>> Handle(ListStopsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        var query = (string.IsNullOrWhiteSpace(request.Search)
                ? stopRepository.QueryNoTracking()
                : stopRepository.SearchByTextNoTracking(request.Search))
            .Where(stop => stop.OperatorId == request.OperatorId && stop.DeletedAt == null);

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
