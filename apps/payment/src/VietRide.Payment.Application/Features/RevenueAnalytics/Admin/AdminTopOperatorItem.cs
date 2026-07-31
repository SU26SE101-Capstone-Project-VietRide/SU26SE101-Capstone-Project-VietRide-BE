namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record AdminTopOperatorItem(
    int Rank,
    Guid OperatorId,
    string OperatorName,
    string? LogoUrl,
    long RevenueVnd,
    int VehicleCount);
