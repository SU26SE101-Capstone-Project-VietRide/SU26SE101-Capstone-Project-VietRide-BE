using MediatR;
using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record GetRouteStopMetricsQuery(Guid OperatorId, Guid RouteId)
    : IRequest<IReadOnlyList<RouteStopMetricDto>>, IQuery<IReadOnlyList<RouteStopMetricDto>>;
