using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

[SkipTransaction]
public sealed record ApproveRouteChangeProposalCommand(Guid OperatorId, Guid ActorUserId, Guid ProposalId) : IRequest<ApproveRouteChangeProposalResponse>;
