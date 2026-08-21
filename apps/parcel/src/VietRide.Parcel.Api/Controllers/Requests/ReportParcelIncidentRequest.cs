using System.Text.Json.Serialization;

namespace VietRide.Parcel.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReportParcelIncidentRequest(
    string IncidentType,
    string? Description,
    IReadOnlyCollection<string>? EvidenceUrls);
