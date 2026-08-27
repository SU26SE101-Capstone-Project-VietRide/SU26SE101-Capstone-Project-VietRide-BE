using System.Text.Json.Serialization;

namespace VietRide.Parcel.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideCustodyExceptionRequest(string Decision, string? Note);
