using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class ListLocationsHandler : IRequestHandler<ListLocationsQuery, IReadOnlyList<LocationDto>>
{
    private readonly ILocationRepository locationRepository;

    public ListLocationsHandler(ILocationRepository locationRepository)
    {
        this.locationRepository = locationRepository;
    }

    public async Task<IReadOnlyList<LocationDto>> Handle(ListLocationsQuery request, CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeType(request.Type);
        if (string.IsNullOrWhiteSpace(request.ParentCode))
        {
            var topLevel = await locationRepository.ListActiveTopLevelAsync(request.Search, cancellationToken);
            return topLevel
                .Where(location => normalizedType is null || location.Type == normalizedType)
                .Select(location => LocationMapper.ToDto(location))
                .ToList();
        }

        var parent = await locationRepository.GetActiveByCodeAsync(request.ParentCode, cancellationToken);
        if (parent is null || !Location.IsTopLevelType(parent.Type))
        {
            throw new ValidationException(
                "Parent location validation failed.",
                [new ValidationError(nameof(request.ParentCode), "Parent location was not found, inactive, or not top-level.")]);
        }

        var children = await locationRepository.ListActiveChildrenAsync(parent.Id, request.Search, cancellationToken);
        return children
            .Where(location => normalizedType is null || location.Type == normalizedType)
            .Select(location => LocationMapper.ToDto(location, parent))
            .ToList();
    }

    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var normalized = type.Trim().ToUpperInvariant();
        if (!Location.IsSupportedType(normalized))
        {
            throw new ValidationException(
                "Location type validation failed.",
                [new ValidationError("type", "Type must be PROVINCE, MUNICIPALITY, WARD, COMMUNE, or SPECIAL_ZONE.")]);
        }

        return normalized;
    }
}
