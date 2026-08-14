using MediatR;
using VietRide.Shared.Kernel.Primitives;
namespace VietRide.Trip.Application.Features.Stations;
public sealed record ListOperatorStationsQuery(
    Guid OperatorId, int? Page, int? PageSize, string? Search,
    bool? IsActive = null, bool? SupportsShuttle = null,
    string? SortBy = null, string? SortDir = null) : IRequest<PagedResult<OperatorStationDto>>;
