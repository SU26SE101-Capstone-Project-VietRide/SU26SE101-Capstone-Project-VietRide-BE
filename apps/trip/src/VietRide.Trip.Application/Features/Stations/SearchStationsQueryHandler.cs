using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class SearchStationsQueryHandler : IRequestHandler<SearchStationsQuery, IReadOnlyList<StationSearchResult>>
{
    private readonly IStationRepository stationRepository;

    public SearchStationsQueryHandler(IStationRepository stationRepository)
    {
        this.stationRepository = stationRepository;
    }

    public async Task<IReadOnlyList<StationSearchResult>> Handle(SearchStationsQuery request, CancellationToken cancellationToken)
    {
        var stations = await stationRepository.SearchActiveByNameAsync(
            request.Q!,
            request.City,
            request.Province,
            cancellationToken);

        return stations.Select(StationMapper.ToSearchResult).ToList();
    }
}
