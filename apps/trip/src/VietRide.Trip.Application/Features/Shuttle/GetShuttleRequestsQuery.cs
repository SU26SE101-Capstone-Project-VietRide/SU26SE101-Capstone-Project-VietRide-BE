using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetShuttleRequestsQuery(Guid OperatorId, int Page, int PageSize)
    : IQuery<ShuttleRequestPage>;
