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
        if (string.IsNullOrWhiteSpace(request.ParentCode))
        {
            var topLevel = await locationRepository.ListActiveTopLevelAsync(request.Search, cancellationToken);
            return topLevel.Select(location => LocationMapper.ToDto(location)).ToList();
        }

        var parent = await locationRepository.GetActiveByCodeAsync(request.ParentCode, cancellationToken);
        if (parent is null || !Location.IsTopLevelType(parent.Type))
        {
            throw new ValidationException(
                "Parent location validation failed.",
                [new ValidationError(nameof(request.ParentCode), "Parent location was not found, inactive, or not top-level.")]);
        }

        var children = await locationRepository.ListActiveChildrenAsync(parent.Id, request.Search, cancellationToken);
        return children.Select(location => LocationMapper.ToDto(location, parent)).ToList();
    }
}
