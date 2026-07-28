using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels;

public sealed record ParcelCargoEstimate(
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal WeightKg,
    decimal VolumeM3,
    decimal DimWeightKg,
    decimal ChargeableWeightKg);

public sealed record ParcelSettlementDeadlines(
    DateTimeOffset LoadCutoffAt,
    DateTimeOffset LatestCheckInAt);

public static class ParcelCargoCalculator
{
    public const decimal DefaultDimWeightFactor = 6000m;
    public const decimal DefaultReweighTolerancePercent = 10m;
    public const decimal DefaultDepositPercent = 20m;
    public const decimal DefaultAutoApproveOverflowPercent = 5m;
    public const int SettlementPolicyVersion = 2;
    public const int DepositPaymentTimeoutMinutes = 15;
    public const int CheckInCloseMinutes = 30;
    public const int FinalPaymentTimeoutMinutes = 30;
    public const int MinimumFinalPaymentWindowMinutes = 10;
    public const int LoadCutoffMinutes = 10;

    public static ParcelCargoEstimate Calculate(
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        decimal weightKg,
        decimal dimWeightFactor)
    {
        if (lengthCm <= 0m || widthCm <= 0m || heightCm <= 0m || weightKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthCm), "Dimensions and weight must be positive.");
        }

        if (dimWeightFactor <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(dimWeightFactor), "DIM weight factor must be positive.");
        }

        var cubicCm = lengthCm * widthCm * heightCm;
        var volumeM3 = Math.Round(cubicCm / 1_000_000m, 4, MidpointRounding.AwayFromZero);
        var dimWeightKg = Math.Round(cubicCm / dimWeightFactor, 2, MidpointRounding.AwayFromZero);
        var normalizedWeight = Math.Round(weightKg, 2, MidpointRounding.AwayFromZero);
        var chargeableWeight = Math.Max(normalizedWeight, dimWeightKg);

        return new ParcelCargoEstimate(
            Math.Round(lengthCm, 2, MidpointRounding.AwayFromZero),
            Math.Round(widthCm, 2, MidpointRounding.AwayFromZero),
            Math.Round(heightCm, 2, MidpointRounding.AwayFromZero),
            normalizedWeight,
            volumeM3,
            dimWeightKg,
            chargeableWeight);
    }

    public static Money CalculateTotalPrice(
        decimal chargeableWeightKg,
        Money pricePerChargeableKg,
        Money minimumPrice)
    {
        var raw = decimal.Round(
            chargeableWeightKg * pricePerChargeableKg.Amount,
            0,
            MidpointRounding.AwayFromZero);
        return Money.FromRaw(Math.Max(minimumPrice.Amount, decimal.ToInt64(raw)));
    }

    public static Money CalculatePercent(Money amount, decimal percent)
    {
        var raw = decimal.Round(
            amount.Amount * percent / 100m,
            0,
            MidpointRounding.AwayFromZero);
        return Money.FromRaw(decimal.ToInt64(raw));
    }

    public static Money CalculateDiscountedTotal(Money grossAmount, Money discountAmount)
        => Money.FromRaw(grossAmount.Amount - Math.Min(discountAmount.Amount, grossAmount.Amount));

    public static ParcelSizeCategory DeriveSizeCategory(decimal chargeableWeightKg)
        => chargeableWeightKg switch
        {
            <= 5m => ParcelSizeCategory.SMALL,
            <= 15m => ParcelSizeCategory.MEDIUM,
            <= 30m => ParcelSizeCategory.LARGE,
            _ => ParcelSizeCategory.EXTRA_LARGE,
        };

    public static ParcelSettlementDeadlines CalculateSettlementDeadlines(DateTimeOffset departureAt)
    {
        var loadCutoffAt = departureAt.AddMinutes(-LoadCutoffMinutes);
        var checkInCloseAt = departureAt.AddMinutes(-CheckInCloseMinutes);
        var paymentWindowCloseAt = loadCutoffAt.AddMinutes(-MinimumFinalPaymentWindowMinutes);
        return new ParcelSettlementDeadlines(
            loadCutoffAt,
            checkInCloseAt <= paymentWindowCloseAt ? checkInCloseAt : paymentWindowCloseAt);
    }

    public static bool IsOutsideTolerance(decimal original, decimal actual, decimal tolerancePercent)
    {
        if (original <= 0m)
        {
            return actual > 0m;
        }

        var deltaPercent = Math.Abs(actual - original) / original * 100m;
        return deltaPercent > tolerancePercent;
    }
}
