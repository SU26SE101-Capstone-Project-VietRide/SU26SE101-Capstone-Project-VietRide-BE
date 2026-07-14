using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class DeleteAdminStationHandler(
    IStationRepository stations,
    IOperatorStationRepository mappings,
    IClock clock,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteAdminStationCommand, StationDto>
{
    public async Task<StationDto> Handle(DeleteAdminStationCommand request, CancellationToken cancellationToken)
    {
        var station = await stations.GetByIdAsync(request.StationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        station.SoftDelete(clock.UtcNow);
        foreach (var mapping in mappings.Query().Where(x => x.StationId == station.Id && x.IsActive))
        {
            mapping.Deactivate();
            mappings.Update(mapping);
        }
        stations.Update(station);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return StationMapper.ToDto(station);
    }
}
