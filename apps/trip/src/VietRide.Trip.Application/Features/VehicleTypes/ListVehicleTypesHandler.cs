using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.VehicleTypes;

public sealed class ListVehicleTypesHandler
    : IRequestHandler<ListVehicleTypesQuery, PagedResult<VehicleTypeDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "displayName",
        "defaultSeatCount",
        "isSystemDefined",
        "createdAt",
        "updatedAt",
    };

    private readonly IVehicleTypeRepository vehicleTypeRepository;

    public ListVehicleTypesHandler(IVehicleTypeRepository vehicleTypeRepository)
    {
        this.vehicleTypeRepository = vehicleTypeRepository;
    }

    public async Task<PagedResult<VehicleTypeDto>> Handle(
        ListVehicleTypesQuery request,
        CancellationToken cancellationToken)
    {
        ValidateSortBy(request.SortBy);
        var page = Math.Max(request.Page ?? DefaultPage, DefaultPage);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        var result = await vehicleTypeRepository.ListActiveAsync(
            page,
            pageSize,
            request.Search,
            request.SearchIn,
            request.SortBy,
            string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
            cancellationToken);

        return PagedResult<VehicleTypeDto>.Create(
            result.Items.Select(VehicleTypeMapper.ToDto).ToList(),
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
