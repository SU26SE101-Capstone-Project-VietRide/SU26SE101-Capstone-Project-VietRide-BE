using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using VietRide.Trip.Application.Abstractions.SeatLock;

namespace VietRide.Trip.Infrastructure.SeatLock;

/// <summary>
/// Redis-backed implementation for short-lived Trip seat locks.
/// </summary>
public sealed class RedisSeatLockStore : ISeatLockStore
{
    private const int DefaultTtlMinutes = 10;
    private const string KeyPrefix = "seat_lock";

    private const string ReleaseIfOwnerScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    public RedisSeatLockStore(
        IConnectionMultiplexer redis,
        IConfiguration configuration)
    {
        _redis = redis;

        var ttlMinutes = configuration.GetValue("SeatLock:TtlMinutes", DefaultTtlMinutes);
        if (ttlMinutes <= 0)
        {
            ttlMinutes = DefaultTtlMinutes;
        }

        _ttl = TimeSpan.FromMinutes(ttlMinutes);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(
        Guid tripId,
        IReadOnlyCollection<string> seatNumbers,
        string lockOwner,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSeatNumbers = NormalizeSeatNumbers(seatNumbers);
        if (normalizedSeatNumbers.Count == 0)
        {
            return true;
        }

        var db = _redis.GetDatabase();
        var acquiredKeys = new List<RedisKey>(normalizedSeatNumbers.Count);

        foreach (var seatNumber in normalizedSeatNumbers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BuildKey(tripId, seatNumber);
            var acquired = await db.StringSetAsync(key, lockOwner, _ttl, When.NotExists);
            if (!acquired)
            {
                await ReleaseAcquiredAsync(db, acquiredKeys, lockOwner);
                return false;
            }

            acquiredKeys.Add(key);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        Guid tripId,
        IReadOnlyCollection<string> seatNumbers,
        string lockOwner,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSeatNumbers = NormalizeSeatNumbers(seatNumbers);
        if (normalizedSeatNumbers.Count == 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        foreach (var seatNumber in normalizedSeatNumbers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReleaseIfOwnerAsync(db, BuildKey(tripId, seatNumber), lockOwner);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLockedAsync(
        Guid tripId,
        string seatNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seatNumber);

        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(BuildKey(tripId, seatNumber.Trim()));
    }

    private static async Task ReleaseAcquiredAsync(
        IDatabase db,
        IReadOnlyCollection<RedisKey> acquiredKeys,
        string lockOwner)
    {
        foreach (var key in acquiredKeys)
        {
            await ReleaseIfOwnerAsync(db, key, lockOwner);
        }
    }

    private static async Task ReleaseIfOwnerAsync(
        IDatabase db,
        RedisKey key,
        string lockOwner)
    {
        await db.ScriptEvaluateAsync(
            ReleaseIfOwnerScript,
            [key],
            [lockOwner]);
    }

    private static IReadOnlyCollection<string> NormalizeSeatNumbers(IReadOnlyCollection<string> seatNumbers)
    {
        ArgumentNullException.ThrowIfNull(seatNumbers);

        return seatNumbers
            .Select(seatNumber => seatNumber.Trim())
            .Where(seatNumber => seatNumber.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildKey(Guid tripId, string seatNumber) =>
        $"{KeyPrefix}:{tripId:D}:{seatNumber}";
}
