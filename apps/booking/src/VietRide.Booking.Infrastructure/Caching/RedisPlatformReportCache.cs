using System.Text.Json;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Caching;
using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Infrastructure.Caching;

internal sealed class RedisPlatformReportCache : IPlatformReportCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _redis;

    public RedisPlatformReportCache(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<PlatformReportResult?> GetAsync(string key, CancellationToken ct = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return value.HasValue
            ? JsonSerializer.Deserialize<PlatformReportResult>(value!, JsonOptions)
            : null;
    }

    public async Task SetAsync(string key, PlatformReportResult value, TimeSpan ttl, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await _redis.GetDatabase().StringSetAsync(key, payload, ttl).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}
