using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

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
        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(request.ParentCode))
        {
            var normalizedParentCode = request.ParentCode.Trim().ToUpperInvariant();
            var parent = locationRepository.QueryNoTracking()
                .FirstOrDefault(location => location.Code.ToUpper() == normalizedParentCode);
            if (parent is null || !Location.IsTopLevelType(parent.Type))
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "parentCode must identify an existing top-level location.",
                    [new ValidationError("parentCode", "Parent location was not found or is not top-level.")]);
            }

            parentId = parent.Id;
        }

        var result = await locationRepository.ListAsync(
            page,
            pageSize,
            request.Search,
            request.IsActive,
            cancellationToken,
            string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToUpperInvariant(),
            parentId);
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
