namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record OperatorRevenuePeriod(
    bool IsYearMode,
    string? Month,
    int? Year,
    DateOnly From,
    DateOnly To,
    DateTimeOffset CurrentFromUtc,
    DateTimeOffset CurrentToUtc,
    DateTimeOffset PreviousFromUtc,
    DateTimeOffset PreviousToUtc,
    DateTimeOffset QueryFromUtc,
    IReadOnlyList<string> Months);
