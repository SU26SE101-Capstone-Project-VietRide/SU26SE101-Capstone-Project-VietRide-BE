namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record AddParcelClaimEvidenceRequest(
    string EvidenceType,
    string Reference,
    string? Note = null);
