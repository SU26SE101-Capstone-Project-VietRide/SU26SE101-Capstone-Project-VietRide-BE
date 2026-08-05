using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateRouteChangeProposalRequest
{
    public required string Type { get; init; }
    public Guid? AlternativeRouteId { get; init; }
    public RouteChangeProposalSnapshotRequest? Route { get; init; }
    public Guid? IncidentId { get; init; }
    public required string Reason { get; init; }
}
