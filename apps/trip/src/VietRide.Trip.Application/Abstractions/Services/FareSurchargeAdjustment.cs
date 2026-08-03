namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record FareSurchargeAdjustment(
    long OriginalFare,
    int SurchargePercent,
    long SurchargeAmount,
    long EffectiveFare,
    Guid? SurchargePeriodId,
    string? SurchargePeriodName);
