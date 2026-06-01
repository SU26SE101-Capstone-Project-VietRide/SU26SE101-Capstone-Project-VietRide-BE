using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Redis-backed OTP rate limiter.
/// Key pattern: <c>identity:otp_rate:{email}</c> — INCR counter with 1-hour TTL.
/// Max 3 sends per hour per email (BSOT §6.9).
/// </summary>
public sealed class RedisOtpRateLimiter : IOtpRateLimiter
{
    private const int MaxSendsPerHour = 3;
    private static readonly TimeSpan WindowTtl = TimeSpan.FromHours(1);

    private readonly IConnectionMultiplexer _redis;

    public RedisOtpRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task<bool> TryIncrementAsync(string email, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"identity:otp_rate:{email.ToLowerInvariant()}";

        var count = await db.StringIncrementAsync(key);

        // Set TTL only on the first increment so the window starts at first send.
        if (count == 1)
            await db.KeyExpireAsync(key, WindowTtl);

        return count <= MaxSendsPerHour;
    }
}
