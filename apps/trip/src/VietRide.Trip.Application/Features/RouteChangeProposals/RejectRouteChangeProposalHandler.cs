using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class RejectRouteChangeProposalHandler : IRequestHandler<RejectRouteChangeProposalCommand, RouteChangeProposalDto>
{
    private readonly IRouteChangeProposalService service;
    public RejectRouteChangeProposalHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<RouteChangeProposalDto> Handle(RejectRouteChangeProposalCommand request, CancellationToken cancellationToken)
        => service.RejectAsync(request.OperatorId, request.ActorUserId, request.ProposalId, request.RejectionReason, cancellationToken);
}
