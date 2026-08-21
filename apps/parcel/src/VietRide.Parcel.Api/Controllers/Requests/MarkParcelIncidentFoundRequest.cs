namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record MarkParcelIncidentFoundRequest(
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Note);
