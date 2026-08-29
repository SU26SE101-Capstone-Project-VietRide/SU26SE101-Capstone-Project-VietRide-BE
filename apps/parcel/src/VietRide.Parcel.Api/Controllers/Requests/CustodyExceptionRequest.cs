using System.Text.Json.Serialization;

namespace VietRide.Parcel.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustodyExceptionRequest(
    string IncidentType,
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    string? TemporaryExceptionTag,
    string? Description,
    decimal? ObservedWeightKg,
    IReadOnlyCollection<string>? EvidenceUrls,
    string Reason);
