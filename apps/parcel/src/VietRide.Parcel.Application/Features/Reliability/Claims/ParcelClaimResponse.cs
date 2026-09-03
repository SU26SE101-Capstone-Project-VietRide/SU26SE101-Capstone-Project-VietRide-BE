using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record ParcelClaimResponse(
    Guid ClaimId,
    Guid ParcelId,
    Guid IncidentId,
    string Status,
    long? DeclaredValueVnd,
    string? ProofStatus,
    long? ProvenDirectLossVnd,
    int CompensationRatePercent,
    long PolicyCapVnd,
    long CargoAwardVnd,
    long FreightRefundVnd,
    long TotalAwardVnd,
    int PolicyVersion,
    Guid BeneficiaryUserId,
    string? DecisionReason,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    Guid? PayoutReferenceId,
    DateTimeOffset? PaidAt,
    string? AppealReason,
    Guid? AppealedByUserId,
    DateTimeOffset? AppealedAt,
    IReadOnlyList<Guid> AcceptedEvidenceIds,
    IReadOnlyList<ParcelClaimEvidenceResponse> Evidence,
    ReliabilityParcelSummaryResponse? ParcelSummary = null,
    ReliabilityIncidentSummaryResponse? IncidentSummary = null,
    ParcelCompensationPolicySnapshotResponse? PolicySnapshot = null,
    DateTimeOffset? DecisionDeadline = null,
    DateTimeOffset? PayoutDeadline = null,
    IReadOnlyList<string>? AvailableActions = null,
    ParcelClaimAppealResponse? Appeal = null);

public sealed record ParcelClaimEvidenceResponse(
    Guid EvidenceId,
    string EvidenceType,
    string Reference,
    string? Note,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAt);
