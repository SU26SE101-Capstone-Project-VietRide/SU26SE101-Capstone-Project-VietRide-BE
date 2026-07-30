using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.Caching;
using VietRide.Parcel.Application.Exceptions;

namespace VietRide.Parcel.Infrastructure.Caching;

internal sealed class RedisDeliveryConfirmationRateLimiter
    : IDeliveryConfirmationRateLimiter
{
    private const int MaximumAttempts = 5;
    private const int WindowSeconds = 60 * 60;
    private const string KeyPrefix = "parcel:delivery_confirm:";
    private const string IncrementScript = """
        local attempts = redis.call('INCR', KEYS[1])
        if attempts == 1 then
          redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return attempts
        """;

    private readonly IConnectionMultiplexer _redis;

    public RedisDeliveryConfirmationRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> TryAcquireAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _redis.GetDatabase()
                .ScriptEvaluateAsync(
                    IncrementScript,
                    [new RedisKey($"{KeyPrefix}{tokenHash}")],
                    [(RedisValue)WindowSeconds])
                .WaitAsync(cancellationToken);

            return (long)result <= MaximumAttempts;
        }
        catch (RedisException exception)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Delivery confirmation rate limiter is unavailable.",
                exception);
        }
    }
}
