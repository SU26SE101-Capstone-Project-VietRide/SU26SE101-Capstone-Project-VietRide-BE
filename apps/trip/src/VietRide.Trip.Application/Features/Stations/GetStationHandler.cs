using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
namespace VietRide.Trip.Application.Features.Stations;
public sealed class GetStationHandler(IStationRepository stationRepository) : IRequestHandler<GetStationQuery, StationDto>
{
    public async Task<StationDto> Handle(GetStationQuery request, CancellationToken cancellationToken)
    {
        var station = await stationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station is null || !station.IsActive)
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        return StationMapper.ToDto(station);
    }
}
