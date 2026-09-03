namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record ParcelClaimAppealResponse(
    Guid AppealId,
    Guid ClaimId,
    string OriginalClaimStatus,
    long OriginalTotalAwardVnd,
    string Status,
    string Reason,
    Guid SubmittedByUserId,
    DateTimeOffset SubmittedAt,
    string? ProofStatus,
    long? RevisedProvenDirectLossVnd,
    long RevisedCargoAwardVnd,
    long RevisedFreightRefundVnd,
    long RevisedTotalAwardVnd,
    long SupplementaryAwardVnd,
    string? DecisionReason,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    Guid? PayoutReferenceId,
    DateTimeOffset? PaidAt,
    IReadOnlyList<Guid> AcceptedEvidenceIds,
    IReadOnlyList<string> AvailableActions);
