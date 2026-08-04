using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListOperatorRouteChangeProposalsHandler : IRequestHandler<ListOperatorRouteChangeProposalsQuery, PagedResult<RouteChangeProposalDto>>
{
    private readonly IRouteChangeProposalService service;
    public ListOperatorRouteChangeProposalsHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<PagedResult<RouteChangeProposalDto>> Handle(ListOperatorRouteChangeProposalsQuery request, CancellationToken cancellationToken)
        => service.ListForOperatorAsync(request.OperatorId, request.TripId, request.Status, request.Type, request.Page, request.PageSize, cancellationToken);
}
