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
        var station = await stationRepository.GetByIdIncludingDeletedAsync(request.Id, cancellationToken);
        if (station is null
            || (station.DeletedAt.HasValue && !station.MergedIntoStationId.HasValue))
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        }

        var isMerged = station.DeletedAt.HasValue && station.MergedIntoStationId.HasValue;
        if (!isMerged && (!station.IsActive || station.MergedIntoStationId.HasValue))
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");

        return new InternalStationDto(
            station.Id,
            station.Name,
            station.Slug,
            station.City,
            station.Ward,
            station.Latitude,
            station.Longitude,
            station.SupportsShuttle,
            station.IsActive,
            isMerged,
            station.MergedIntoStationId ?? station.Id,
            station.CreatedAt,
            station.UpdatedAt);
    }
}
