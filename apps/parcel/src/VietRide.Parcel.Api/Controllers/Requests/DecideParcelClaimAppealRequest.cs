namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DecideParcelClaimAppealRequest(
    string Decision,
    string? ProofStatus,
    long? RevisedProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds,
    string Reason);
