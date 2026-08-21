using System.Text.Json.Serialization;

namespace VietRide.Parcel.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParcelCustodyScanRequest(
    string ParcelCode,
    string EventType,
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Reason);
