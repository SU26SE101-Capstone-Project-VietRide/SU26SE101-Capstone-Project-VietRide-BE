using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Routes;

public sealed record SearchInternalRoutesQuery(Guid OperatorId, string Search)
    : IRequest<InternalRouteSearchDto>;
