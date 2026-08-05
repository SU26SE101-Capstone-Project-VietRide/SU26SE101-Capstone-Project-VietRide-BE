using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class GetOperatorRouteChangeProposalHandler : IRequestHandler<GetOperatorRouteChangeProposalQuery, RouteChangeProposalDto>
{
    private readonly IRouteChangeProposalService service;
    public GetOperatorRouteChangeProposalHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<RouteChangeProposalDto> Handle(GetOperatorRouteChangeProposalQuery request, CancellationToken cancellationToken)
        => service.GetForOperatorAsync(request.OperatorId, request.ProposalId, cancellationToken);
}
