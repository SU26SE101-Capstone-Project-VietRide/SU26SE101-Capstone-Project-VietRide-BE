using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record ListAdminStopsQuery(Guid? OperatorId, int? Page, int? PageSize, string? Search, bool? IsActive)
    : IRequest<PagedResult<StopDto>>;
