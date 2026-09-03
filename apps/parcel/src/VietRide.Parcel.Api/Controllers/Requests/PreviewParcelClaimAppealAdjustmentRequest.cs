namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record PreviewParcelClaimAppealAdjustmentRequest(
    string? ProofStatus,
    long? RevisedProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds);
