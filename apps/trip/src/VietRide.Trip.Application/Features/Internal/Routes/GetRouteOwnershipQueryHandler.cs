using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Routes;

public sealed class GetRouteOwnershipQueryHandler : IRequestHandler<GetRouteOwnershipQuery, RouteOwnershipDto>
{
    private readonly IRouteRepository _routeRepository;

    public GetRouteOwnershipQueryHandler(IRouteRepository routeRepository)
    {
        _routeRepository = routeRepository;
    }

    public async Task<RouteOwnershipDto> Handle(
        GetRouteOwnershipQuery request,
        CancellationToken cancellationToken)
    {
        var owned = await _routeRepository.ExistsActiveOwnedByOperatorAsync(
            request.OperatorId,
            request.RouteId,
            cancellationToken);
        if (!owned)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        return new RouteOwnershipDto(request.RouteId, request.OperatorId);
    }
}
