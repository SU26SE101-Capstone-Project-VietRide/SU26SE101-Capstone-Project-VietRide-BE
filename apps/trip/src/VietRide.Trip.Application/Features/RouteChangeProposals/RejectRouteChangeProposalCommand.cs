using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

[SkipTransaction]
public sealed record RejectRouteChangeProposalCommand(Guid OperatorId, Guid ActorUserId, Guid ProposalId, string? RejectionReason) : IRequest<RouteChangeProposalDto>;
