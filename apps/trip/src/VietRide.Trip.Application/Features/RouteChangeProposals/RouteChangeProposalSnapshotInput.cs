namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record RouteChangeProposalSnapshotInput(
    string Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    string? PathPolyline,
    IReadOnlyList<RouteChangeProposalStopSnapshot> Stops);
