using StackExchange.Redis;

namespace VietRide.Payment.Infrastructure.Caching;

public interface IRevenueCacheStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, TimeSpan expiration, CancellationToken cancellationToken);
}

internal sealed class RedisRevenueCacheStore : IRevenueCacheStore
{
    private readonly IConnectionMultiplexer redis;

    public RedisRevenueCacheStore(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public async Task SetAsync(
        string key,
        string value,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().StringSetAsync(key, value, expiration).ConfigureAwait(false);
    }
}
