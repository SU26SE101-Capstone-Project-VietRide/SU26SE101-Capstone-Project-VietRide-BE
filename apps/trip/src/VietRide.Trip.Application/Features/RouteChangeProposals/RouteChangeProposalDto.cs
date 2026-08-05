namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record RouteChangeProposalDto(
    Guid Id,
    Guid TripId,
    Guid OperatorId,
    Guid ProposedByUserId,
    string Type,
    string Status,
    Guid? SourceAlternativeRouteId,
    DateTimeOffset? SourceUpdatedAt,
    Guid? IncidentId,
    string Reason,
    RouteChangeProposalSnapshotInput Snapshot,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    string? ResolutionCode,
    Guid? SupersededByProposalId,
    Guid? ApprovedAlternativeRouteId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
