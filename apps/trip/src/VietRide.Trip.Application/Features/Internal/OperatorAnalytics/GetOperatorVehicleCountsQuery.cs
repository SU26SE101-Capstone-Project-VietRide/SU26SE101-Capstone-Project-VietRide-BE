using MediatR;

namespace VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

public sealed record GetOperatorVehicleCountsQuery(IReadOnlyList<Guid> OperatorIds)
    : IRequest<IReadOnlyList<OperatorVehicleCountResponse>>;
