using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetOperatorShuttleTripsQuery(
    Guid OperatorId,
    int Page,
    int PageSize,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyCollection<string>? Statuses)
    : IQuery<PagedResult<OperatorShuttleTripListItemDto>>;
