using MediatR;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record CreateRouteChangeProposalCommand(
    Guid TripId,
    Guid UserId,
    string Type,
    Guid? AlternativeRouteId,
    RouteChangeProposalSnapshotInput? CustomRoute,
    Guid? IncidentId,
    string Reason) : IRequest<RouteChangeProposalDto>;
