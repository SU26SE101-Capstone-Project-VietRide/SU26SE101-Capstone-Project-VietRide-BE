using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetShuttleAssignmentHistoryQuery(
    Guid OperatorId,
    Guid ShuttleTripId,
    int Page,
    int PageSize) : IQuery<PagedResult<ShuttleAssignmentHistoryItemDto>>;
