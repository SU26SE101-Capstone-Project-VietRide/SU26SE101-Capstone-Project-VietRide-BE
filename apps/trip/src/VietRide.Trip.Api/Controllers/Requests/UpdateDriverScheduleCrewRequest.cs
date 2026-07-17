using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateDriverScheduleCrewRequest(
    Guid DriverUserId,
    Guid? AssistantUserId);
