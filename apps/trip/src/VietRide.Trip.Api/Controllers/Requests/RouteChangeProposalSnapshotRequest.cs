using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RouteChangeProposalSnapshotRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Guid DestinationStationId { get; init; }
    public decimal? TotalDistanceKm { get; init; }
    public int? EstimatedDurationMinutes { get; init; }
    public required string PathPolyline { get; init; }
    public IReadOnlyList<RouteChangeProposalStopRequest> Stops { get; init; } = [];
}
