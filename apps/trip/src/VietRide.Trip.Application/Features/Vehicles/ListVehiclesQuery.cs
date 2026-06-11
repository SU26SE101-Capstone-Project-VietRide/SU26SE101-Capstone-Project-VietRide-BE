using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record ListVehiclesQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize,
    string? Search,
    string? SearchIn,
    string? SortBy,
    string? SortDir)
    : IRequest<PagedResult<VehicleDto>>;
