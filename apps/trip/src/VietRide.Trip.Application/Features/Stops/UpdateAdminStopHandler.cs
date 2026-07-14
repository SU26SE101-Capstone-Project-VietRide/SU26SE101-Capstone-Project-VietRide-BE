using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class UpdateAdminStopHandler(IStopRepository stops, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateAdminStopCommand, StopDto>
{
    public async Task<StopDto> Handle(UpdateAdminStopCommand request, CancellationToken cancellationToken)
    {
        var stop = await stops.GetByIdAsync(request.StopId, cancellationToken)
            ?? throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        stop.UpdateDetails(request.Name ?? stop.Name, request.Latitude ?? stop.Latitude,
            request.Longitude ?? stop.Longitude, request.Description ?? stop.Description, stop.LocationId,
            request.Address ?? stop.Address, request.GooglePlaceId ?? stop.GooglePlaceId);
        if (request.IsActive == true) stop.Activate();
        if (request.IsActive == false) stop.Deactivate();
        stops.Update(stop);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return StopMapper.ToDto(stop);
    }
}
