using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

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

    public ListVehiclesHandler(IVehicleRepository vehicleRepository)
    {
        this.vehicleRepository = vehicleRepository;
    }

    public async Task<PagedResult<VehicleDto>> Handle(
        ListVehiclesQuery request,
        CancellationToken cancellationToken)
    {
        ValidateSortBy(request.SortBy);
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
            cancellationToken);

        return PagedResult<VehicleDto>.Create(
            result.Items.Select(VehicleMapper.ToDto).ToList(),
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
