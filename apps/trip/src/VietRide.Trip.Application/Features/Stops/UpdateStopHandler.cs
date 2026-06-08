using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class UpdateStopHandler : IRequestHandler<UpdateStopCommand, StopDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateStopHandler(
        IIdentityInternalClient identityInternalClient,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<StopDto> Handle(UpdateStopCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var stop = stopRepository.Query()
            .FirstOrDefault(stop =>
                stop.Id == request.StopId
                && stop.OperatorId == request.OperatorId
                && stop.DeletedAt == null);

        if (stop is null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }

        stop.UpdateDetails(
            request.Name ?? stop.Name,
            request.Latitude ?? stop.Latitude,
            request.Longitude ?? stop.Longitude,
            request.Description ?? stop.Description,
            request.Address ?? stop.Address,
            request.GooglePlaceId ?? stop.GooglePlaceId);

        stopRepository.Update(stop);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StopMapper.ToDto(stop);
    }
}
