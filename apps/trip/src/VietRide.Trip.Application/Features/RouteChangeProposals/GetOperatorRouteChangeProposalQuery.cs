using MediatR;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record GetOperatorRouteChangeProposalQuery(Guid OperatorId, Guid ProposalId) : IRequest<RouteChangeProposalDto>;
