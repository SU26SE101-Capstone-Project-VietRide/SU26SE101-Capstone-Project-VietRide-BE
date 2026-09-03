using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class PreviewParcelClaimAwardQueryHandler
    : IRequestHandler<PreviewParcelClaimAwardQuery, ParcelCompensationPreviewResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public PreviewParcelClaimAwardQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability)
    {
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<ParcelCompensationPreviewResponse> Handle(
        PreviewParcelClaimAwardQuery query,
        CancellationToken cancellationToken)
    {
        var claim = await _reliability.GetClaimByIdAsync(query.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.OperatorId != query.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        if (claim.Status != ParcelClaimStatus.SUBMITTED)
            throw new CodedConflictException("PARCEL_CLAIM_ALREADY_DECIDED", "Claim has already been decided.");

        var parcel = await _parcels.GetByIdAsync(claim.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        var proof = await ParcelClaimProofAssessmentValidator.ValidateAsync(
            query.ProofStatus,
            query.ProvenDirectLossVnd,
            query.AcceptedEvidenceIds,
            claim.Id,
            _reliability,
            cancellationToken);
        var award = ParcelCompensationCalculator.Calculate(
            proof.ProofStatus,
            proof.ProvenDirectLossVnd,
            parcel.DeclaredValueVnd,
            parcel.FinalTotalPriceVnd.Amount,
            parcel.RefundedAmountVnd.Amount,
            claim.CompensationRatePercent,
            claim.PolicyCapVnd,
            claim.NoProofFallbackMultiplier);
        // A read-only preview may explain a zero award; only the approval mutation requires
        // a positive payable total. In particular, unverified proof plus fully refunded freight is zero.
        return BuildResponse(claim, parcel, proof, award);
    }

    internal static ParcelCompensationPreviewResponse BuildResponse(
        ParcelClaim claim,
        ParcelEntity parcel,
        ValidatedParcelClaimProof proof,
        ParcelCompensationAward award,
        Guid? appealId = null,
        long? originalTotalAwardVnd = null,
        long? supplementaryAwardVnd = null)
        => new(
            claim.Id,
            appealId,
            proof.ProofStatus.ToString(),
            proof.AcceptedEvidenceIds,
            award.CalculationBasis,
            proof.ProvenDirectLossVnd,
            award.AssessedLossVnd,
            award.DeclaredLiabilityVnd,
            award.FallbackAmountVnd,
            new ParcelCompensationPolicySnapshotResponse(
                claim.PolicyVersion,
                claim.CompensationRatePercent,
                claim.PolicyCapVnd,
                claim.NoProofFallbackMultiplier,
                parcel.ClaimWindowDaysSnapshot,
                parcel.SearchSlaHoursSnapshot,
                parcel.DecisionSlaBusinessDaysSnapshot,
                parcel.PayoutSlaBusinessDaysSnapshot),
            award.CargoAwardVnd,
            award.FreightRefundVnd,
            award.TotalAwardVnd,
            originalTotalAwardVnd,
            supplementaryAwardVnd);
}
