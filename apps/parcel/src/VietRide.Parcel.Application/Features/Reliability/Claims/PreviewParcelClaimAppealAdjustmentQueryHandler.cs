using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class PreviewParcelClaimAppealAdjustmentQueryHandler
    : IRequestHandler<PreviewParcelClaimAppealAdjustmentQuery, ParcelCompensationPreviewResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public PreviewParcelClaimAppealAdjustmentQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability)
    {
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<ParcelCompensationPreviewResponse> Handle(
        PreviewParcelClaimAppealAdjustmentQuery query,
        CancellationToken cancellationToken)
    {
        var appeal = await _reliability.GetClaimAppealByIdAsync(query.AppealId, cancellationToken);
        if (appeal is null || appeal.OperatorId != query.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_APPEAL_NOT_FOUND", "Claim appeal was not found.");
        if (appeal.Status != ParcelClaimAppealStatus.SUBMITTED)
            throw new CodedConflictException(
                "PARCEL_CLAIM_APPEAL_ALREADY_DECIDED",
                "Claim appeal has already been decided.");

        var claim = await _reliability.GetClaimByIdAsync(appeal.ClaimId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_CLAIM_NOT_FOUND", "Claim was not found.");
        var parcel = await _parcels.GetByIdAsync(appeal.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (claim.OperatorId != query.OperatorId || parcel.OperatorId != query.OperatorId)
            throw new CodedNotFoundException("PARCEL_CLAIM_APPEAL_NOT_FOUND", "Claim appeal was not found.");

        var proof = await ParcelClaimProofAssessmentValidator.ValidateAsync(
            query.ProofStatus,
            query.RevisedProvenDirectLossVnd,
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
        var supplementaryAwardVnd = checked(award.TotalAwardVnd - appeal.OriginalTotalAwardVnd);
        if (supplementaryAwardVnd <= 0)
        {
            throw new CodedValidationException(
                "PARCEL_CLAIM_APPEAL_ADJUSTMENT_REQUIRED",
                "An approved appeal must increase the total compensation award.");
        }

        return PreviewParcelClaimAwardQueryHandler.BuildResponse(
            claim,
            parcel,
            proof,
            award,
            appeal.Id,
            appeal.OriginalTotalAwardVnd,
            supplementaryAwardVnd);
    }
}
