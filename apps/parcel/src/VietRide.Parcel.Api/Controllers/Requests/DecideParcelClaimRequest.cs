namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record DecideParcelClaimRequest(
    string Decision,
    string? ProofStatus,
    long? ProvenDirectLossVnd,
    IReadOnlyList<Guid>? AcceptedEvidenceIds,
    string Reason);
