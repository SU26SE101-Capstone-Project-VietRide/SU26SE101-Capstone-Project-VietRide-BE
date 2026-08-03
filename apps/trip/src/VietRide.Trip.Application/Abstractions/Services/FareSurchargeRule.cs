namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record FareSurchargeRule(
    Guid PeriodId,
    string PeriodName,
    int Percent);
