using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class GetRouteHandler : IRequestHandler<GetRouteQuery, RouteDto>
{
    private readonly IRouteRepository routeRepository;

    public GetRouteHandler(IRouteRepository routeRepository)
    {
        this.routeRepository = routeRepository;
    }

    public async Task<RouteDto> Handle(GetRouteQuery request, CancellationToken cancellationToken)
    {
        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        return RouteMapper.ToDto(route);
    }
}
