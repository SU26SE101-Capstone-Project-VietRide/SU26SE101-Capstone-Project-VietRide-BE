namespace VietRide.Payment.Application.Abstractions.Services;

public interface IRevenueReportCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class;
}
