using MediatR;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
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

        var stop = Stop.Create(
            request.OperatorId,
            request.Name!,
            request.Latitude!.Value,
            request.Longitude!.Value,
            request.Description,
            request.Address,
            request.GooglePlaceId,
            await ResolveLocationIdAsync(request.LocationId, request.LocationCode, cancellationToken));

        await stopRepository.AddAsync(stop, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StopMapper.ToDto(stop);
    }

    private async Task<Guid?> ResolveLocationIdAsync(Guid? locationId, string? locationCode, CancellationToken cancellationToken)
    {
        if (locationId.HasValue)
        {
            if (locationRepository is null)
            {
                throw new InvalidOperationException("Location repository is required when locationId is provided.");
            }

            var location = await locationRepository.GetActiveByIdAsync(locationId.Value, cancellationToken);
            if (location is null)
            {
                throw new VietRide.Shared.Application.Exceptions.ValidationException(
                    "Location logical FK validation failed.",
                    [new VietRide.Shared.Application.Exceptions.ValidationError("locationId", "Location was not found or inactive.")]);
            }

            return location.Id;
        }

        if (string.IsNullOrWhiteSpace(locationCode))
        {
            return null;
        }

        if (locationRepository is null)
        {
            throw new InvalidOperationException("Location repository is required when locationCode is provided.");
        }

        var locationByCode = await locationRepository.GetActiveByCodeAsync(locationCode, cancellationToken);
        if (locationByCode is null)
        {
            throw new VietRide.Shared.Application.Exceptions.ValidationException(
                "Location logical FK validation failed.",
                [new VietRide.Shared.Application.Exceptions.ValidationError("locationCode", "Location was not found or inactive.")]);
        }

        return locationByCode.Id;
    }
}
