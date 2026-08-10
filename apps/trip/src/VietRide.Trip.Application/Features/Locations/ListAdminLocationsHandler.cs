using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class ListAdminLocationsHandler : IRequestHandler<ListAdminLocationsQuery, PagedResult<LocationDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly ILocationRepository locationRepository;

    public ListAdminLocationsHandler(ILocationRepository locationRepository)
    {
        this.locationRepository = locationRepository;
    }

    public async Task<PagedResult<LocationDto>> Handle(
        ListAdminLocationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page ?? DefaultPage, DefaultPage);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var result = await locationRepository.ListAsync(
            page,
            pageSize,
            request.Search,
            request.IsActive,
            cancellationToken);
        var parentIds = result.Items
            .Where(location => location.ParentLocationId.HasValue)
            .Select(location => location.ParentLocationId!.Value)
            .ToHashSet();
        var parentsById = locationRepository.QueryNoTracking()
            .Where(location => parentIds.Contains(location.Id))
            .ToDictionary(location => location.Id);

        return PagedResult<LocationDto>.Create(
            result.Items
                .Select(location => LocationMapper.ToDto(
                    location,
                    location.ParentLocationId.HasValue
                        && parentsById.TryGetValue(location.ParentLocationId.Value, out var parent)
                            ? parent
                            : null))
                .ToList(),
            result.Page,
            result.PageSize,
            result.TotalItems);
    }
}
