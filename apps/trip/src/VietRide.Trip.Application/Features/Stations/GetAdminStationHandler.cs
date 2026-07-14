using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class GetAdminStationHandler(IStationRepository stations) : IRequestHandler<GetAdminStationQuery, StationDto>
{
    public async Task<StationDto> Handle(GetAdminStationQuery request, CancellationToken cancellationToken)
    {
        var station = await stations.GetByIdAsync(request.StationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        return StationMapper.ToDto(station);
    }
}
