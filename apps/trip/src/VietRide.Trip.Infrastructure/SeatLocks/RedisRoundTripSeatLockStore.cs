using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Infrastructure.SeatLocks;

internal sealed class RedisRoundTripSeatLockStore : IRoundTripSeatLockStore
{
    private const string SeatLockPrefix = "seat_lock";
    private const string IdempotencyPrefix = "trip:round_trip_lock:idempotency";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string LockScript = """
        local idem_key = KEYS[1]
        local existing = redis.call('GET', idem_key)
        if existing then
            return {'REPLAY', existing}
        end

        local ttl_ms = tonumber(ARGV[1])
        local payload = ARGV[2]
        local unavailable = {}
        local seat_count = #KEYS - 1

        for i = 1, seat_count do
            local key = KEYS[i + 1]
            if redis.call('EXISTS', key) == 1 then
                unavailable[#unavailable + 1] = ARGV[2 + seat_count + i]
            end
        end

        if #unavailable > 0 then
            return {'CONFLICT', table.concat(unavailable, ',')}
        end

        for i = 1, seat_count do
            redis.call('PSETEX', KEYS[i + 1], ttl_ms, ARGV[2 + i])
        end

        redis.call('PSETEX', idem_key, ttl_ms, payload)
        return {'OK', payload}
        """;

    private readonly IConnectionMultiplexer redis;

    public RedisRoundTripSeatLockStore(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<RoundTripSeatLockStoreResult> TryLockAsync(
        RoundTripSeatLockStoreRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = redis.GetDatabase();
        var expiresAt = DateTimeOffset.UtcNow.Add(request.Ttl);
        var payload = BuildReplayPayload(request, expiresAt);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var seatOwners = BuildSeatOwners(request);
        var seatLabels = request.Outbound.SeatNumbers
            .Select(seat => $"outbound.seatNumbers|{seat}")
            .Concat(request.Return.SeatNumbers.Select(seat => $"return.seatNumbers|{seat}"))
            .ToArray();

        var keys = new List<RedisKey> { IdempotencyKey(request.IdempotencyKey) };
        keys.AddRange(request.Outbound.SeatNumbers.Select(seat => SeatKey(request.Outbound.TripId, seat)));
        keys.AddRange(request.Return.SeatNumbers.Select(seat => SeatKey(request.Return.TripId, seat)));

        var args = new List<RedisValue>
        {
            (long)request.Ttl.TotalMilliseconds,
            payloadJson,
        };
        args.AddRange(seatOwners.Select(owner => (RedisValue)owner));
        args.AddRange(seatLabels.Select(seat => (RedisValue)seat));

        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
            LockScript,
            keys.ToArray(),
            args.ToArray()).ConfigureAwait(false);

        if (result is not { Length: 2 })
        {
            throw new InvalidOperationException("Redis round-trip lock script returned an unexpected shape.");
        }

        var status = (string?)result[0] ?? string.Empty;
        var value = (string?)result[1] ?? string.Empty;

        return status switch
        {
            "OK" => new RoundTripSeatLockStoreResult(false, true, [], BuildReplay(value, request)),
            "REPLAY" => BuildReplayResult(value, request),
            "CONFLICT" => new RoundTripSeatLockStoreResult(
                false,
                false,
                value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ParseConflict)
                    .ToArray(),
                null),
            _ => throw new InvalidOperationException("Redis round-trip lock script returned an unknown status."),
        };
    }

    public async Task ReleaseAsync(
        IReadOnlyList<RoundTripSeatLockKey> keys,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var redisKeys = keys
            .Select(key => SeatKey(key.TripId, key.SeatNumber))
            .Prepend(IdempotencyKey(idempotencyKey))
            .ToArray();

        if (redisKeys.Length > 0)
        {
            await redis.GetDatabase().KeyDeleteAsync(redisKeys).ConfigureAwait(false);
        }
    }

    private static RoundTripSeatLockStoreResult BuildReplayResult(
        string payloadJson,
        RoundTripSeatLockStoreRequest request)
        => new(true, true, [], BuildReplay(payloadJson, request));

    private static RoundTripSeatLockReplay BuildReplay(
        string payloadJson,
        RoundTripSeatLockStoreRequest request)
    {
        var payload = JsonSerializer.Deserialize<RedisRoundTripSeatLockPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Redis idempotency payload is invalid.");

        if (!string.Equals(payload.RequestHash, ComputeRequestHash(request), StringComparison.Ordinal))
        {
            throw new CodedValidationException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was reused with a different round-trip lock request.",
                [new ValidationError("Idempotency-Key", "Idempotency-Key was reused with a different request body.")]);
        }

        return new RoundTripSeatLockReplay(payload.Outbound, payload.Return);
    }

    private static RedisRoundTripSeatLockPayload BuildReplayPayload(
        RoundTripSeatLockStoreRequest request,
        DateTimeOffset expiresAt)
        => new(
            ComputeRequestHash(request),
            new RoundTripSeatLockReplayLeg(
                request.Outbound.TripId,
                request.Outbound.SeatLockToken,
                request.Outbound.SeatNumbers,
                expiresAt),
            new RoundTripSeatLockReplayLeg(
                request.Return.TripId,
                request.Return.SeatLockToken,
                request.Return.SeatNumbers,
                expiresAt));

    // Store the leg's seat-lock token (canonical "D" form) as each seat key's value, IDENTICAL to the
    // single-trip RedisSeatLockStore. The round-trip book step reuses the single-trip BookSeatsHandler,
    // whose IsOwnedByAsync compares the stored value to SeatLockToken.ToString("D"); a richer JSON value
    // would never match, so both legs would fail to confirm. Keep the formats aligned.
    private static IReadOnlyList<string> BuildSeatOwners(RoundTripSeatLockStoreRequest request)
        => request.Outbound.SeatNumbers
            .Select(_ => request.Outbound.SeatLockToken.ToString("D"))
            .Concat(request.Return.SeatNumbers.Select(_ => request.Return.SeatLockToken.ToString("D")))
            .ToArray();

    private static string ComputeRequestHash(RoundTripSeatLockStoreRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            outbound = new { request.Outbound.TripId, request.Outbound.SeatNumbers },
            @return = new { request.Return.TripId, request.Return.SeatNumbers },
            request.HoldOwnerId,
            ttlSeconds = (int)request.Ttl.TotalSeconds,
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static RedisKey IdempotencyKey(string idempotencyKey)
        => $"{IdempotencyPrefix}:{idempotencyKey}";

    private static RedisKey SeatKey(Guid tripId, string seatNumber)
        => $"{SeatLockPrefix}:{tripId:D}:{seatNumber}";

    private static RoundTripSeatConflict ParseConflict(string value)
    {
        var separator = value.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException("Redis round-trip lock conflict payload is invalid.");
        }

        return new RoundTripSeatConflict(value[..separator], value[(separator + 1)..]);
    }

    private sealed record RedisRoundTripSeatLockPayload(
        string RequestHash,
        RoundTripSeatLockReplayLeg Outbound,
        RoundTripSeatLockReplayLeg Return);
}
