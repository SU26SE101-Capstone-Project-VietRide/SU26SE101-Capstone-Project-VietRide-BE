using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

public sealed class RedisLoginLockoutCounter : ILoginLockoutCounter
{
    private static readonly TimeSpan WindowTtl = TimeSpan.FromMinutes(15);

    private readonly IConnectionMultiplexer _redis;

    public RedisLoginLockoutCounter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task<long> IncrementAsync(Guid userId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = BuildKey(userId);

        var count = await db.StringIncrementAsync(key);

        // Set TTL only on the first increment so the 15-minute window starts at
        // the first wrong password, matching BSOT §6.8 / Redis key registry.
        if (count == 1)
            await db.KeyExpireAsync(key, WindowTtl);

        return count;
    }

    /// <inheritdoc />
    public async Task ResetAsync(Guid userId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(BuildKey(userId));
    }

    private static string BuildKey(Guid userId)
        => $"identity:login_lockout:{userId}";
}
