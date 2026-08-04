using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListDriverRouteChangeProposalsHandler : IRequestHandler<ListDriverRouteChangeProposalsQuery, PagedResult<RouteChangeProposalDto>>
{
    private readonly IRouteChangeProposalService service;
    public ListDriverRouteChangeProposalsHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<PagedResult<RouteChangeProposalDto>> Handle(ListDriverRouteChangeProposalsQuery request, CancellationToken cancellationToken)
        => service.ListForAssignedCrewAsync(request.TripId, request.UserId, request.Type, request.Page, request.PageSize, cancellationToken);
}
