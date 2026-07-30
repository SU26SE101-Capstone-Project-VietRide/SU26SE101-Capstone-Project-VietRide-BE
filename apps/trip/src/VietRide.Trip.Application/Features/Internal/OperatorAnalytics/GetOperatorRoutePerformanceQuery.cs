using MediatR;

namespace VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

public sealed record GetOperatorRoutePerformanceQuery(Guid OperatorId, string? Month)
    : IRequest<IReadOnlyList<OperatorRoutePerformanceResponse>>;
