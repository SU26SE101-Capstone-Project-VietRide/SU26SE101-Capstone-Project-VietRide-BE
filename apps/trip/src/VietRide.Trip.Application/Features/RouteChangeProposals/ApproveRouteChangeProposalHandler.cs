using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ApproveRouteChangeProposalHandler : IRequestHandler<ApproveRouteChangeProposalCommand, ApproveRouteChangeProposalResponse>
{
    private readonly IRouteChangeProposalService service;
    public ApproveRouteChangeProposalHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<ApproveRouteChangeProposalResponse> Handle(ApproveRouteChangeProposalCommand request, CancellationToken cancellationToken)
        => service.ApproveAsync(request.OperatorId, request.ActorUserId, request.ProposalId, cancellationToken);
}
