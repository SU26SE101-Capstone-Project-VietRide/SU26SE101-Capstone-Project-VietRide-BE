using VietRide.Trip.Application.Features.Trips;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record ApproveRouteChangeProposalResponse(
    RouteChangeProposalDto Proposal,
    ChangeTripRouteResponse RouteChange);
