using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class GetAlternativeRouteHandler : IRequestHandler<GetAlternativeRouteQuery, AlternativeRouteDto>
{
    private readonly IAlternativeRouteRepository alternativeRouteRepository;

    public GetAlternativeRouteHandler(IAlternativeRouteRepository alternativeRouteRepository)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
    }

    public async Task<AlternativeRouteDto> Handle(
        GetAlternativeRouteQuery request,
        CancellationToken cancellationToken)
    {
        var alternativeRoute = await alternativeRouteRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.AlternativeRouteId,
            cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
        var stops = await alternativeRouteRepository.ListStopsAsync(
            alternativeRoute.Id,
            cancellationToken);

        return AlternativeRouteMapper.ToDto(alternativeRoute, stops);
    }
}
