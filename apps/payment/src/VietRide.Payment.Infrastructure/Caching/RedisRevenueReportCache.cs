using System.Text.Json;
using VietRide.Payment.Application.Abstractions.Services;

namespace VietRide.Payment.Infrastructure.Caching;

public sealed class RedisRevenueReportCache : IRevenueReportCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRevenueCacheStore store;

    public RedisRevenueReportCache(IRevenueCacheStore store)
    {
        this.store = store;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return value is null ? null : JsonSerializer.Deserialize<T>(value, JsonOptions);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await store.SetAsync(key, payload, expiration, cancellationToken).ConfigureAwait(false);
    }
}
