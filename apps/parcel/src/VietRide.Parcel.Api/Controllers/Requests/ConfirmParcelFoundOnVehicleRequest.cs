using System.Text.Json.Serialization;

namespace VietRide.Parcel.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmParcelFoundOnVehicleRequest(
    Guid IncidentId,
    string ParcelCode,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Note);
