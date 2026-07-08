using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

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
        var locations = await locationRepository.ListActiveAsync(cancellationToken);
        return locations.Select(LocationMapper.ToDto).ToList();
    }
}
