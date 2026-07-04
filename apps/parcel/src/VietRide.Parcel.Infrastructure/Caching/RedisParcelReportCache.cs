using System.Text.Json;
using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.Caching;

namespace VietRide.Parcel.Infrastructure.Caching;

internal sealed class RedisParcelReportCache : IParcelReportCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer redis;

    public RedisParcelReportCache(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase().StringGetAsync(key);
        cancellationToken.ThrowIfCancellationRequested();
        return value.HasValue
            ? JsonSerializer.Deserialize<T>(value!, JsonOptions)
            : default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await redis.GetDatabase().StringSetAsync(key, payload, ttl);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
