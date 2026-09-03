using VietRide.Parcel.Application.Features.Parcels.Create;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record ParcelCompensationPreviewResponse(
    Guid ClaimId,
    Guid? AppealId,
    string ProofStatus,
    IReadOnlyList<Guid> AcceptedEvidenceIds,
    string CalculationBasis,
    long? ProvenDirectLossVnd,
    long? AssessedLossVnd,
    long? DeclaredLiabilityVnd,
    long? FallbackAmountVnd,
    ParcelCompensationPolicySnapshotResponse PolicySnapshot,
    long CargoAwardVnd,
    long FreightRefundVnd,
    long TotalAwardVnd,
    long? OriginalTotalAwardVnd = null,
    long? SupplementaryAwardVnd = null);
