using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RejectRouteChangeProposalRequest
{
    public string? Reason { get; init; }
}
