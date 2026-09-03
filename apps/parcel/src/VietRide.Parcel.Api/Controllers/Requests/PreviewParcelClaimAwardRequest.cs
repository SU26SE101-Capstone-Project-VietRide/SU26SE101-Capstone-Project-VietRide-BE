namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record PreviewParcelClaimAwardRequest(
    string? ProofStatus,
    long? ProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds);
