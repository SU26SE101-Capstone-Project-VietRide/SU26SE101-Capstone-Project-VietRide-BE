using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public static class RouteChangeProposalMapper
{
    public static RouteChangeProposalDto ToDto(RouteChangeProposal proposal)
        => new(
            proposal.Id,
            proposal.TripId,
            proposal.OperatorId,
            proposal.ProposedByUserId,
            proposal.Type.ToString(),
            proposal.Status.ToString(),
            proposal.SourceAlternativeRouteId,
            proposal.SourceUpdatedAt,
            proposal.IncidentId,
            proposal.Reason,
            new RouteChangeProposalSnapshotInput(
                proposal.Name,
                proposal.Description,
                proposal.DestinationStationId,
                proposal.TotalDistanceKm,
                proposal.EstimatedDurationMinutes,
                proposal.PathPolyline,
                proposal.Stops.OrderBy(stop => stop.OrderIndex).ThenBy(stop => stop.StopId).Select(ToSnapshotStop).ToArray()),
            proposal.DecidedByUserId,
            proposal.DecidedAt,
            proposal.RejectionReason,
            proposal.ResolutionCode,
            proposal.SupersededByProposalId,
            proposal.ApprovedAlternativeRouteId,
            proposal.CreatedAt,
            proposal.UpdatedAt);

    private static RouteChangeProposalStopSnapshot ToSnapshotStop(RouteChangeProposalStop stop)
        => new(stop.StopId, stop.OrderIndex, stop.EstimatedDurationFromOriginMinutes, stop.DistanceFromOriginKm);
}
