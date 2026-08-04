using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RouteChangeProposalStopRequest
{
    public Guid StopId { get; init; }
    public int OrderIndex { get; init; }
    public int EstimatedDurationFromOriginMinutes { get; init; }
    public decimal? DistanceFromOriginKm { get; init; }
}
