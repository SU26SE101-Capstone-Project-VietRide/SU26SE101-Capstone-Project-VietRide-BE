using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

internal static class ParcelClaimAppealResponseMapper
{
    public static ParcelClaimAppealResponse Map(ParcelClaimAppeal appeal, bool operatorView)
    {
        var actions = new List<string>();
        if (operatorView && appeal.Status == ParcelClaimAppealStatus.SUBMITTED)
            actions.Add("DECIDE_APPEAL");

        return new ParcelClaimAppealResponse(
            appeal.Id,
            appeal.ClaimId,
            appeal.OriginalClaimStatus.ToString(),
            appeal.OriginalTotalAwardVnd,
            appeal.Status.ToString(),
            appeal.Reason,
            appeal.SubmittedByUserId,
            appeal.SubmittedAt,
            appeal.RevisedProvenDirectLossVnd,
            appeal.RevisedCargoAwardVnd,
            appeal.RevisedFreightRefundVnd,
            appeal.RevisedTotalAwardVnd,
            appeal.SupplementaryAwardVnd,
            appeal.DecisionReason,
            operatorView ? appeal.DecidedByUserId : null,
            appeal.DecidedAt,
            appeal.PayoutReferenceId,
            appeal.PaidAt,
            actions);
    }
}
