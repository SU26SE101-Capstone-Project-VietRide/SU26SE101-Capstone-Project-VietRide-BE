using VietRide.Shared.Application.Cqrs;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed record GetAdminRevenueAnalyticsQuery(
    string? From,
    string? To,
    string? GroupBy,
    int? Top) : IQuery<AdminRevenueAnalyticsResponse>;
