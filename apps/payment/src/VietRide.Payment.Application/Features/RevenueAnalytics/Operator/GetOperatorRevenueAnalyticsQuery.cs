using VietRide.Shared.Application.Cqrs;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record GetOperatorRevenueAnalyticsQuery(
    Guid OperatorId,
    string? Month) : IQuery<OperatorRevenueAnalyticsResponse>;
