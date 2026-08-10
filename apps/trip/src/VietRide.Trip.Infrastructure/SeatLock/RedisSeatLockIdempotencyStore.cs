using System.Text.Json;
using StackExchange.Redis;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

namespace VietRide.Trip.Infrastructure.SeatLock;

public sealed class RedisSeatLockIdempotencyStore : ISeatLockIdempotencyStore
{
    private const string KeyPrefix = "trip:idem:lock-seats";
    private const string StoreCompletedScript = "local current = redis.call('GET', KEYS[1]) if not current then return 0 end local decoded = cjson.decode(current) if decoded.requestFingerprint ~= ARGV[1] then return -1 end if decoded.reservationToken ~= ARGV[2] then return -2 end if decoded.result ~= cjson.null then return -3 end redis.call('SET', KEYS[1], ARGV[3], 'EX', ARGV[4]) return 1";
    private const string RemoveReservationScript = "local current = redis.call('GET', KEYS[1]) if not current then return 0 end local decoded = cjson.decode(current) if decoded.reservationToken ~= ARGV[1] then return 0 end if decoded.result ~= cjson.null then return 0 end return redis.call('DEL', KEYS[1])";
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    private readonly IConnectionMultiplexer redis;

    public RedisSeatLockIdempotencyStore(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<SeatLockIdempotencyEntry?> GetAsync(
        Guid tripId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        var payload = await redis.GetDatabase().StringGetAsync(BuildKey(tripId, idempotencyKey));
        if (!payload.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<SeatLockIdempotencyEntry>(payload!, JsonOptions);
    }

    public async Task<SeatLockIdempotencyReservation> TryReserveAsync(
        Guid tripId,
        string idempotencyKey,
        string requestFingerprint,
        IReadOnlyCollection<string> normalizedSeatNumbers,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentNullException.ThrowIfNull(normalizedSeatNumbers);

        cancellationToken.ThrowIfCancellationRequested();

        var reservationToken = Guid.NewGuid().ToString("D");
        var entry = new SeatLockIdempotencyEntry(requestFingerprint, normalizedSeatNumbers.ToArray(), null, reservationToken);
        var payload = JsonSerializer.Serialize(entry, JsonOptions);
        var reserved = await redis.GetDatabase().StringSetAsync(BuildKey(tripId, idempotencyKey), payload, ttl, When.NotExists);
        return reserved
            ? new SeatLockIdempotencyReservation(true, reservationToken, null)
            : new SeatLockIdempotencyReservation(false, null, await GetAsync(tripId, idempotencyKey, cancellationToken));
    }

    public async Task<bool> StoreCompletedAsync(
        Guid tripId,
        string idempotencyKey,
        string requestFingerprint,
        string expectedReservationToken,
        IReadOnlyCollection<string> normalizedSeatNumbers,
        LockSeatsResult result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReservationToken);
        ArgumentNullException.ThrowIfNull(normalizedSeatNumbers);
        ArgumentNullException.ThrowIfNull(result);

        cancellationToken.ThrowIfCancellationRequested();

        var entry = new SeatLockIdempotencyEntry(requestFingerprint, normalizedSeatNumbers.ToArray(), result, expectedReservationToken);
        var payload = JsonSerializer.Serialize(entry, JsonOptions);
        var response = await redis.GetDatabase().ScriptEvaluateAsync(
            StoreCompletedScript,
            [BuildKey(tripId, idempotencyKey)],
            [requestFingerprint, expectedReservationToken, payload, (long)ttl.TotalSeconds]);

        return response is RedisResult redisResult && !redisResult.IsNull && (long)redisResult == 1;
    }

    public async Task RemoveReservationAsync(
        Guid tripId,
        string idempotencyKey,
        string expectedReservationToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReservationToken);

        cancellationToken.ThrowIfCancellationRequested();

        await redis.GetDatabase().ScriptEvaluateAsync(
            RemoveReservationScript,
            [BuildKey(tripId, idempotencyKey)],
            [expectedReservationToken]);
    }

    private static string BuildKey(Guid tripId, string idempotencyKey) =>
        $"{KeyPrefix}:{tripId:D}:{idempotencyKey.Trim()}";
}
