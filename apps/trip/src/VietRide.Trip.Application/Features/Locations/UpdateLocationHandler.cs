using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class UpdateLocationHandler : IRequestHandler<UpdateLocationCommand, LocationDto>
{
    private readonly ILocationRepository locationRepository;
    private readonly IUnitOfWork unitOfWork;

    public UpdateLocationHandler(ILocationRepository locationRepository, IUnitOfWork unitOfWork)
    {
        this.locationRepository = locationRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<LocationDto> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await locationRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new CodedNotFoundException("LOCATION_NOT_FOUND", "Location was not found.");

        var code = request.Code?.Trim().ToUpperInvariant() ?? location.Code;
        if (request.Code is not null
            && await locationRepository.ExistsByCodeAsync(code, request.Id, cancellationToken))
        {
            throw new ConflictException("LOCATION_CODE_CONFLICT", "A location with the same code already exists.");
        }

        var targetType = (request.Type ?? location.Type).Trim().ToUpperInvariant();
        LocationHierarchyGuard.ValidateOfficialCode(code, targetType);
        var parent = await LocationHierarchyGuard.ResolveParentAsync(
            locationRepository,
            targetType,
            request.ParentCode,
            location.ParentLocationId,
            cancellationToken);

        location.UpdateDetails(
            code,
            request.Name ?? location.Name,
            targetType,
            parent?.Id,
            request.SortOrder ?? location.SortOrder);
        if (request.IsActive == true)
        {
            location.Activate();
        }
        else if (request.IsActive == false)
        {
            location.Deactivate();
        }

        locationRepository.Update(location);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return LocationMapper.ToDto(location, parent);
    }
}
