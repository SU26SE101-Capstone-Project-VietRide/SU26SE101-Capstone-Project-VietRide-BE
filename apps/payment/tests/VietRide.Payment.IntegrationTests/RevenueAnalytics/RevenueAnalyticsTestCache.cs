using VietRide.Payment.Application.Abstractions.Services;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

internal sealed class RevenueAnalyticsTestCache : IRevenueReportCache
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class
        => Task.CompletedTask;
}
