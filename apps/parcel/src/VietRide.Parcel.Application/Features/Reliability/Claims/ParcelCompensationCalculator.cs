using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public static class ParcelCompensationCalculator
{
    public static ParcelCompensationAward Calculate(
        long? provenDirectLossVnd,
        long? declaredValueVnd,
        long freightCollectedVnd,
        long alreadyRefundedVnd,
        int compensationRatePercent,
        long policyCapVnd,
        int noProofFallbackMultiplier)
    {
        if (provenDirectLossVnd is < 0
            || declaredValueVnd is < 0
            || freightCollectedVnd < 0
            || alreadyRefundedVnd < 0)
            throw new CodedValidationException("VALIDATION_ERROR", "Compensation inputs cannot be negative.");
        if (compensationRatePercent is < 1 or > 100
            || policyCapVnd <= 0
            || noProofFallbackMultiplier <= 0)
            throw new CodedValidationException("VALIDATION_ERROR", "Compensation policy is invalid.");

        var freightRefundVnd = Math.Max(0, freightCollectedVnd - alreadyRefundedVnd);
        long cargoAwardVnd;
        long? assessedLossVnd = null;

        if (!provenDirectLossVnd.HasValue)
        {
            var fallback = decimal.Multiply(freightCollectedVnd, noProofFallbackMultiplier);
            cargoAwardVnd = (long)Math.Min(fallback, policyCapVnd);
        }
        else
        {
            assessedLossVnd = declaredValueVnd.HasValue
                ? Math.Min(provenDirectLossVnd.Value, declaredValueVnd.Value)
                : provenDirectLossVnd.Value;
            var gross = decimal.Round(
                decimal.Multiply(assessedLossVnd.Value, compensationRatePercent) / 100m,
                0,
                MidpointRounding.AwayFromZero);
            cargoAwardVnd = (long)Math.Min(gross, policyCapVnd);
        }

        return new ParcelCompensationAward(
            assessedLossVnd,
            cargoAwardVnd,
            freightRefundVnd,
            checked(cargoAwardVnd + freightRefundVnd));
    }
}

public sealed record ParcelCompensationAward(
    long? AssessedLossVnd,
    long CargoAwardVnd,
    long FreightRefundVnd,
    long TotalAwardVnd);
