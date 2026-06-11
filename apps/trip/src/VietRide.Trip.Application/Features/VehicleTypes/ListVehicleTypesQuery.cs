using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.VehicleTypes;

public sealed record ListVehicleTypesQuery(
    int? Page,
    int? PageSize,
    string? Search,
    string? SearchIn,
    string? SortBy,
    string? SortDir)
    : IRequest<PagedResult<VehicleTypeDto>>;
