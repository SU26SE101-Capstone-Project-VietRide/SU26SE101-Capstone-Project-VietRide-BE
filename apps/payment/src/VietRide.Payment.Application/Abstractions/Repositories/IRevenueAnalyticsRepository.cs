using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IRevenueAnalyticsRepository
{
    Task<IReadOnlyList<AdminRevenueMonthReadModel>> GetAdminMonthlyRevenueAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopOperatorRevenueReadModel>> GetTopOperatorRevenueAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int top,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperatorRevenueLedgerReadModel>> GetOperatorRevenueLedgerAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<OperatorRevenueSummaryReadModel> GetOperatorRevenueSummaryAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
