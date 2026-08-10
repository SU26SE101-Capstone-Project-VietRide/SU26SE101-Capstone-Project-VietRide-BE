using MediatR;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Locations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class CreateStopHandler : IRequestHandler<CreateStopCommand, StopDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly ILocationRepository? locationRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateStopHandler(
        IIdentityInternalClient identityInternalClient,
        ILocationRepository locationRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.locationRepository = locationRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public CreateStopHandler(
        IIdentityInternalClient identityInternalClient,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
        : this(identityInternalClient, null!, stopRepository, unitOfWork)
    {
    }

    public async Task<StopDto> Handle(CreateStopCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        if (locationRepository is null)
        {
            throw new InvalidOperationException("Location repository is required when creating a stop.");
        }

        var location = await LocationHierarchyGuard.ResolveActiveLeafAsync(
            locationRepository,
            request.LocationId,
            request.LocationCode,
            nameof(request.LocationId),
            nameof(request.LocationCode),
            cancellationToken);
        var stop = Stop.Create(
            request.OperatorId,
            request.Name!,
            request.Latitude!.Value,
            request.Longitude!.Value,
            request.Description,
            request.Address,
            request.GooglePlaceId,
            location.Leaf.Id);

        await stopRepository.AddAsync(stop, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StopMapper.ToDto(stop, StopLocationContextResolver.From(location.Leaf, location.Parent));
    }

}
