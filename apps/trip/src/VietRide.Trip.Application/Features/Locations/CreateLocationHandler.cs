using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

public sealed class CreateLocationHandler : IRequestHandler<CreateLocationCommand, LocationDto>
{
    private readonly ILocationRepository locationRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateLocationHandler(ILocationRepository locationRepository, IUnitOfWork unitOfWork)
    {
        this.locationRepository = locationRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<LocationDto> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code!.Trim().ToUpperInvariant();
        if (await locationRepository.ExistsByCodeAsync(code, null, cancellationToken))
        {
            throw new ConflictException("LOCATION_CODE_CONFLICT", "A location with the same code already exists.");
        }

        var type = request.Type!.Trim().ToUpperInvariant();
        LocationHierarchyGuard.ValidateOfficialCode(code, type);
        var parent = await LocationHierarchyGuard.ResolveParentAsync(
            locationRepository,
            type,
            request.ParentCode,
            null,
            cancellationToken);
        var location = Location.Create(
            code,
            request.Name!,
            type,
            parent?.Id,
            request.SortOrder ?? 0,
            request.IsActive);

        await locationRepository.AddAsync(location, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return LocationMapper.ToDto(location, parent);
    }
}
