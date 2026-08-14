using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetShuttleRequestsQuery(
    Guid OperatorId, int Page, int PageSize, DateOnly? From = null, DateOnly? To = null,
    Guid? MainTripId = null, string? Search = null)
    : IQuery<PagedResult<ShuttleRequestTripGroup>>;
