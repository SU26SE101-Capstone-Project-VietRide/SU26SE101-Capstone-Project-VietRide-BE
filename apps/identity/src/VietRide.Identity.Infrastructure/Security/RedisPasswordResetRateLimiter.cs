using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Redis-backed password-reset OTP rate limiter.
/// </summary>
public sealed class RedisPasswordResetRateLimiter : IPasswordResetRateLimiter
{
    private const int MaxSendsPerHour = 3;
    private static readonly TimeSpan WindowTtl = TimeSpan.FromHours(1);

    private readonly IConnectionMultiplexer _redis;

    public RedisPasswordResetRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryIncrementAsync(string email, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"identity:pwd_reset_rate:{email.ToLowerInvariant()}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, WindowTtl);

        return count <= MaxSendsPerHour;
    }
}
