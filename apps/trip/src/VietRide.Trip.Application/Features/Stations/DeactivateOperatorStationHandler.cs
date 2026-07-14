using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class DeactivateOperatorStationHandler : IRequestHandler<DeactivateOperatorStationCommand, OperatorStationDto>
{
    private readonly IOperatorStationRepository mappings;
    private readonly IStationRepository stations;
    private readonly IUnitOfWork unitOfWork;

    public DeactivateOperatorStationHandler(IOperatorStationRepository mappings, IStationRepository stations, IUnitOfWork unitOfWork)
    {
        this.mappings = mappings;
        this.stations = stations;
        this.unitOfWork = unitOfWork;
    }

    public async Task<OperatorStationDto> Handle(DeactivateOperatorStationCommand request, CancellationToken cancellationToken)
    {
        var mapping = mappings.Query().FirstOrDefault(x => x.OperatorId == request.OperatorId && x.StationId == request.StationId)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Operator station was not found.");
        var station = stations.QueryNoTracking().FirstOrDefault(x => x.Id == request.StationId)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        mapping.Deactivate();
        mappings.Update(mapping);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UpdateOperatorStationHandler.ToDto(mapping, station);
    }
}
