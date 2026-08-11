using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class SearchStationsQueryHandler : IRequestHandler<SearchStationsQuery, IReadOnlyList<StationSearchResult>>
{
    private readonly IStationRepository stationRepository;
    private readonly ILocationRepository? locationRepository;

    public SearchStationsQueryHandler(
        IStationRepository stationRepository,
        ILocationRepository? locationRepository = null)
    {
        this.stationRepository = stationRepository;
        this.locationRepository = locationRepository;
    }

    public async Task<IReadOnlyList<StationSearchResult>> Handle(SearchStationsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Station> stations;
        if (string.IsNullOrWhiteSpace(request.LocationScopeCode))
        {
            stations = await stationRepository.SearchActiveByNameAsync(
                request.Q,
                request.City,
                request.Ward,
                request.LocationId,
                cancellationToken);
        }
        else
        {
            var locationIds = await ResolveLocationScopeAsync(request.LocationScopeCode, cancellationToken);
            stations = await stationRepository.SearchActiveByNameInLocationsAsync(
                request.Q,
                request.City,
                request.Ward,
                locationIds,
                cancellationToken);
        }

        return stations.Select(StationMapper.ToSearchResult).ToList();
    }

    private async Task<IReadOnlyCollection<Guid>> ResolveLocationScopeAsync(
        string locationScopeCode,
        CancellationToken cancellationToken)
    {
        if (locationRepository is null)
        {
            throw new InvalidOperationException("Location repository is required for hierarchy station search.");
        }

        var code = locationScopeCode.Trim();
        var location = await locationRepository.GetActiveByCodeAsync(code, cancellationToken);
        if (location is null
            || code.Length == 2 && (!Location.IsTopLevelType(location.Type) || location.ParentLocationId.HasValue)
            || code.Length == 5 && (!Location.IsLeafType(location.Type) || !location.ParentLocationId.HasValue))
        {
            throw new ValidationException(
                "Location scope was not found or inactive.",
                [new ValidationError(
                    nameof(SearchStationsQuery.LocationScopeCode),
                    "Location scope was not found, inactive, or has an invalid hierarchy type.")]);
        }

        if (Location.IsLeafType(location.Type))
        {
            return [location.Id];
        }

        var children = await locationRepository.ListActiveChildrenAsync(location.Id, null, cancellationToken);
        return children
            .Where(child => Location.IsLeafType(child.Type))
            .Select(child => child.Id)
            .Append(location.Id)
            .ToHashSet();
    }
}
