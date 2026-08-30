using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

internal static class ParcelClaimResponseMapper
{
    public static async Task<ParcelClaimResponse> MapAsync(
        ParcelClaim claim,
        IParcelReliabilityRepository reliability,
        CancellationToken cancellationToken,
        ParcelEntity? parcel = null,
        ParcelIncident? incident = null,
        bool operatorView = false,
        DateTimeOffset? now = null,
        ParcelClaimAppeal? appealOverride = null)
    {
        var evidence = await reliability.ListClaimEvidenceAsync(claim.Id, cancellationToken);
        var appeal = appealOverride
            ?? await reliability.GetClaimAppealByClaimAsync(claim.Id, cancellationToken);
        incident ??= await reliability.GetIncidentAsync(claim.IncidentId, cancellationToken);
        var at = now ?? DateTimeOffset.UtcNow;
        DateTimeOffset? decisionDeadline = parcel is not null
            && claim.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW
                ? BusinessDayDeadline.Add(claim.CreatedAt, parcel.DecisionSlaBusinessDaysSnapshot)
                : null;
        DateTimeOffset? payoutDeadline = parcel is not null
            && claim.Status == ParcelClaimStatus.APPROVED
            && claim.DecidedAt.HasValue
                ? BusinessDayDeadline.Add(claim.DecidedAt.Value, parcel.PayoutSlaBusinessDaysSnapshot)
                : null;
        var actions = new List<string>();
        if (operatorView && claim.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            actions.Add("DECIDE_CLAIM");
        if (!operatorView && claim.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW)
            actions.Add("ADD_EVIDENCE");
        if (!operatorView
            && appeal is null
            && claim.Status is ParcelClaimStatus.PAID or ParcelClaimStatus.REJECTED)
            actions.Add("APPEAL");

        return new ParcelClaimResponse(
            claim.Id,
            claim.ParcelId,
            claim.IncidentId,
            claim.Status.ToString(),
            claim.DeclaredValueVnd,
            claim.ProvenDirectLossVnd,
            claim.CompensationRatePercent,
            claim.PolicyCapVnd,
            claim.CargoAwardVnd,
            claim.FreightRefundVnd,
            claim.TotalAwardVnd,
            claim.PolicyVersion,
            claim.BeneficiaryUserId,
            claim.DecisionReason,
            claim.DecidedBy,
            claim.DecidedAt,
            claim.PayoutReferenceId,
            claim.PaidAt,
            claim.AppealReason,
            claim.AppealedByUserId,
            claim.AppealedAt,
            evidence.Select(x => new ParcelClaimEvidenceResponse(
                x.Id,
                x.EvidenceType,
                x.Reference,
                x.Note,
                x.UploadedByUserId,
                x.CreatedAt)).ToArray(),
            parcel is null
                ? null
                : new ReliabilityParcelSummaryResponse(
                    parcel.Id,
                    parcel.ParcelCode,
                    parcel.Status.ToString(),
                    parcel.Description,
                    parcel.PhotoUrl,
                    parcel.Quantity,
                    parcel.DeclaredValueVnd),
            ParcelReliabilityReadModelService.MapIncident(incident, at),
            parcel is null
                ? null
                : new ParcelCompensationPolicySnapshotResponse(
                    claim.PolicyVersion,
                    claim.CompensationRatePercent,
                    claim.PolicyCapVnd,
                    claim.NoProofFallbackMultiplier,
                    parcel.ClaimWindowDaysSnapshot,
                    parcel.SearchSlaHoursSnapshot,
                    parcel.DecisionSlaBusinessDaysSnapshot,
                    parcel.PayoutSlaBusinessDaysSnapshot),
            decisionDeadline,
            payoutDeadline,
            actions,
            appeal is null ? null : ParcelClaimAppealResponseMapper.Map(appeal, operatorView));
    }
}
