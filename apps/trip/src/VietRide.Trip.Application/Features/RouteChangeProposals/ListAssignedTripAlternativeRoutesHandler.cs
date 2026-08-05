using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.AlternativeRoutes;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListAssignedTripAlternativeRoutesHandler : IRequestHandler<ListAssignedTripAlternativeRoutesQuery, PagedResult<AlternativeRouteDto>>
{
    private readonly IRouteChangeProposalService service;
    public ListAssignedTripAlternativeRoutesHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<PagedResult<AlternativeRouteDto>> Handle(ListAssignedTripAlternativeRoutesQuery request, CancellationToken cancellationToken)
        => service.ListAlternativeRoutesForAssignedCrewAsync(request.TripId, request.UserId, request.Page, request.PageSize, cancellationToken);
}
