using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Stations;

public sealed class GetStationByIdHandler : IRequestHandler<GetStationByIdQuery, InternalStationDto>
{
    private readonly IStationRepository stationRepository;

    public GetStationByIdHandler(IStationRepository stationRepository)
    {
        this.stationRepository = stationRepository;
    }

    public async Task<InternalStationDto> Handle(GetStationByIdQuery request, CancellationToken cancellationToken)
    {
        var station = await stationRepository.GetByIdAsync(request.Id, cancellationToken);
        if (station is null)
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        }

        return new InternalStationDto(
            station.Id,
            station.Name,
            station.Slug,
            station.City,
            station.Province,
            station.Latitude,
            station.Longitude,
            station.IsActive,
            station.CreatedAt,
            station.UpdatedAt);
    }
}
