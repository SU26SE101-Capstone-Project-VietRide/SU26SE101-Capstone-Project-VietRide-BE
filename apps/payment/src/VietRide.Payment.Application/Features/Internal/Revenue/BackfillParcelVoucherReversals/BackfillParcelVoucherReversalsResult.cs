namespace VietRide.Payment.Application.Features.Internal.Revenue.BackfillParcelVoucherReversals;

public sealed record BackfillParcelVoucherReversalsResult(
    int ScannedRefundCount,
    int CandidateCount,
    int SkippedExistingCount,
    int LegacyUnclassifiedCount,
    long TotalAdjustmentVnd,
    int AppliedCount);
