using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public static class ParcelCompensationCalculator
{
    public static ParcelCompensationAward Calculate(
        ParcelClaimProofStatus proofStatus,
        long? provenDirectLossVnd,
        long? declaredValueVnd,
        long freightCollectedVnd,
        long alreadyRefundedVnd,
        int compensationRatePercent,
        long policyCapVnd,
        int noProofFallbackMultiplier)
    {
        if (!Enum.IsDefined(proofStatus)
            || provenDirectLossVnd is < 0
            || declaredValueVnd is < 0
            || freightCollectedVnd < 0
            || alreadyRefundedVnd < 0)
            throw new CodedValidationException("VALIDATION_ERROR", "Compensation inputs cannot be negative.");
        if (compensationRatePercent is < 1 or > 100
            || policyCapVnd <= 0
            || noProofFallbackMultiplier <= 0)
            throw new CodedValidationException("VALIDATION_ERROR", "Compensation policy is invalid.");
        if (proofStatus == ParcelClaimProofStatus.VERIFIED && !provenDirectLossVnd.HasValue)
            throw new CodedValidationException(
                "PARCEL_CLAIM_EVIDENCE_REQUIRED",
                "Verified proof requires a proven direct loss.");
        if (proofStatus != ParcelClaimProofStatus.VERIFIED && provenDirectLossVnd.HasValue)
            throw new CodedValidationException(
                "PARCEL_CLAIM_EVIDENCE_REQUIRED",
                "Unverified or missing proof cannot carry a proven direct loss.");

        var freightRefundVnd = Math.Max(0, freightCollectedVnd - alreadyRefundedVnd);
        long cargoAwardVnd;
        long? assessedLossVnd = null;
        long? declaredLiabilityVnd = declaredValueVnd.HasValue
            ? RoundRate(declaredValueVnd.Value, compensationRatePercent)
            : null;
        string calculationBasis;

        if (proofStatus != ParcelClaimProofStatus.VERIFIED)
        {
            // Self-declaration never establishes loss. Legacy multiplier snapshots are retained
            // for audit/compatibility only and cannot enable a new unverified cargo award.
            cargoAwardVnd = 0;
            calculationBasis = "NO_VERIFIED_PROOF_FREIGHT_ONLY";
        }
        else
        {
            var provenLossVnd = provenDirectLossVnd.GetValueOrDefault();
            var assessed = declaredValueVnd.HasValue
                ? Math.Min(provenLossVnd, declaredValueVnd.Value)
                : provenLossVnd;
            assessedLossVnd = assessed;
            var gross = RoundRate(assessed, compensationRatePercent);
            cargoAwardVnd = Math.Min(gross, policyCapVnd);
            calculationBasis = "VERIFIED_LOSS";
        }

        return new ParcelCompensationAward(
            calculationBasis,
            assessedLossVnd,
            declaredLiabilityVnd,
            null,
            cargoAwardVnd,
            freightRefundVnd,
            AddAmounts(cargoAwardVnd, freightRefundVnd));
    }

    private static long AddAmounts(long cargoAwardVnd, long freightRefundVnd)
    {
        try
        {
            return checked(cargoAwardVnd + freightRefundVnd);
        }
        catch (OverflowException)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Compensation amount exceeds the supported range.");
        }
    }

    private static long RoundRate(long amountVnd, int compensationRatePercent)
        => (long)decimal.Round(
            decimal.Multiply(amountVnd, compensationRatePercent) / 100m,
            0,
            MidpointRounding.AwayFromZero);
}

public sealed record ParcelCompensationAward(
    string CalculationBasis,
    long? AssessedLossVnd,
    long? DeclaredLiabilityVnd,
    long? FallbackAmountVnd,
    long CargoAwardVnd,
    long FreightRefundVnd,
    long TotalAwardVnd);
