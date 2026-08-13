using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed class ListVehiclesHandler : IRequestHandler<ListVehiclesQuery, PagedResult<VehicleDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "licensePlate",
        "totalSeats",
        "status",
        "isActive",
        "createdAt",
        "updatedAt",
    };

    private readonly IVehicleRepository vehicleRepository;
    private readonly IResourceAvailabilityService? resourceAvailability;
    private readonly IClock? clock;

    public ListVehiclesHandler(
        IVehicleRepository vehicleRepository,
        IResourceAvailabilityService? resourceAvailability = null,
        IClock? clock = null)
    {
        this.vehicleRepository = vehicleRepository;
        this.resourceAvailability = resourceAvailability;
        this.clock = clock;
    }

    public async Task<PagedResult<VehicleDto>> Handle(
        ListVehiclesQuery request,
        CancellationToken cancellationToken)
    {
        ValidateSortBy(request.SortBy);
        var status = string.IsNullOrWhiteSpace(request.Status)
            ? (VehicleStatus?)null
            : Enum.Parse<VehicleStatus>(request.Status, ignoreCase: true);
        var page = Math.Max(request.Page ?? DefaultPage, DefaultPage);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var result = await vehicleRepository.ListByOperatorAsync(
            request.OperatorId,
            page,
            pageSize,
            request.Search,
            request.SearchIn,
            request.SortBy,
            string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
            cancellationToken,
            request.VehicleTypeId,
            status,
            request.IsActive);

        var assignments = resourceAvailability is null
            ? new Dictionary<Guid, (VehicleAssignmentProjection? Current, VehicleAssignmentProjection? Next)>()
            : await resourceAvailability.GetVehicleAssignmentsAsync(
                request.OperatorId,
                result.Items.Select(item => item.Id).ToArray(),
                clock?.UtcNow ?? DateTimeOffset.UtcNow,
                cancellationToken);

        return PagedResult<VehicleDto>.Create(
            result.Items.Select(vehicle =>
            {
                var assignment = assignments.GetValueOrDefault(vehicle.Id);
                return VehicleMapper.ToDto(vehicle, assignment.Current, assignment.Next);
            }).ToList(),
            result.Page,
            result.PageSize,
            result.TotalItems);
    }

    private static void ValidateSortBy(string? sortBy)
    {
        if (!string.IsNullOrWhiteSpace(sortBy) && !AllowedSortFields.Contains(sortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", $"Unsupported sort field '{sortBy}'.");
    }
}
