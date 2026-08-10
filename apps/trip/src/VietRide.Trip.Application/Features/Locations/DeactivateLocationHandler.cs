using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class DeactivateLocationHandler : IRequestHandler<DeactivateLocationCommand, LocationDto>
{
    private readonly ILocationRepository locationRepository;
    private readonly IUnitOfWork unitOfWork;

    public DeactivateLocationHandler(ILocationRepository locationRepository, IUnitOfWork unitOfWork)
    {
        this.locationRepository = locationRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<LocationDto> Handle(DeactivateLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await locationRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new CodedNotFoundException("LOCATION_NOT_FOUND", "Location was not found.");

        location.Deactivate();
        locationRepository.Update(location);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var parent = location.ParentLocationId.HasValue
            ? await locationRepository.GetByIdAsync(location.ParentLocationId.Value, cancellationToken)
            : null;
        return LocationMapper.ToDto(location, parent);
    }
}
