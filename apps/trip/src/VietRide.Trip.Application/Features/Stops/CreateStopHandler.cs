using MediatR;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class CreateStopHandler : IRequestHandler<CreateStopCommand, StopDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateStopHandler(
        IIdentityInternalClient identityInternalClient,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<StopDto> Handle(CreateStopCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var stop = Stop.Create(
            request.OperatorId,
            request.Name!,
            request.Latitude!.Value,
            request.Longitude!.Value,
            request.Description,
            request.Address,
            request.GooglePlaceId);

        await stopRepository.AddAsync(stop, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StopMapper.ToDto(stop);
    }
}
