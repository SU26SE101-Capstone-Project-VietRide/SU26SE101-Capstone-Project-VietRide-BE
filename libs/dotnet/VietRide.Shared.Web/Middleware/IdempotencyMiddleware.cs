using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Shared.Web.Middleware;

/// <summary>
/// Redis-backed idempotency middleware. Endpoints opt in to mandatory key validation with
/// <see cref="RequireIdempotencyAttribute"/>; legacy endpoints without a key retain pass-through behavior.
/// </summary>
public sealed class IdempotencyMiddleware
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string MismatchErrorCode = "IDEMPOTENCY_KEY_MISMATCH";
    public const string PendingErrorCode = "IDEMPOTENCY_REQUEST_PENDING";

    private const int CompletedTtlSeconds = 86400;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string CompleteReservationScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            return 0
        end
        local ok, entry = pcall(cjson.decode, current)
        if ok
            and entry.state == 'pending'
            and entry.requestFingerprint == ARGV[1]
            and entry.reservationToken == ARGV[2] then
            redis.call('SET', KEYS[1], ARGV[3], 'EX', ARGV[4])
            return 1
        end
        return 0
        """;

    private const string ReleaseReservationScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            return 0
        end
        local ok, entry = pcall(cjson.decode, current)
        if ok
            and entry.state == 'pending'
            and entry.reservationToken == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        IdempotencyOptions options,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var idempotencyMetadata = context.GetEndpoint()?.Metadata.GetMetadata<RequireIdempotencyAttribute>();
        var requiresIdempotency = idempotencyMetadata is not null;
        if (!requiresIdempotency
            && !HttpMethods.IsPost(context.Request.Method)
            && !HttpMethods.IsPatch(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!TryReadHeaderValue(context, out var key))
        {
            if (requiresIdempotency)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_ERROR",
                    "A valid UUID v4 Idempotency-Key header is required.");
                return;
            }

            await _next(context);
            return;
        }

        if (requiresIdempotency && !TryNormalizeUuidV4(key, out key))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                "A valid UUID v4 Idempotency-Key header is required.");
            return;
        }

        if (!requiresIdempotency)
        {
            await InvokeLegacyAsync(context, key);
            return;
        }

        if (!idempotencyMetadata!.AllowRequestBody
            && await HasNonEmptyRequestBodyAsync(context.Request, context.RequestAborted))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                "The request body must be empty.");
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true
            && string.IsNullOrWhiteSpace(IdempotencyFingerprint.ResolveSubject(context.User)))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                "Authentication required");
            return;
        }

        var fingerprint = await IdempotencyFingerprint.ComputeAsync(context);
        var database = _redis.GetDatabase();
        var redisKey = (RedisKey)$"{_options.ServicePrefix}:idem:{key}";
        var reservationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var reservation = new StoredEntry("pending", fingerprint, reservationToken, null, null);
        var reservationPayload = JsonSerializer.Serialize(reservation, JsonOptions);

        var reserved = await database.StringSetAsync(
            redisKey,
            reservationPayload,
            TimeSpan.FromSeconds(CompletedTtlSeconds),
            When.NotExists,
            CommandFlags.None);

        if (!reserved)
        {
            await HandleExistingAsync(context, database, redisKey, fingerprint);
            return;
        }

        await ExecuteReservedAsync(
            context,
            database,
            redisKey,
            reservationPayload,
            reservationToken,
            fingerprint);
    }

    private static async Task<bool> HasNonEmptyRequestBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        var originalPosition = request.Body.Position;
        var probe = new byte[1];

        try
        {
            return await request.Body.ReadAsync(probe.AsMemory(), cancellationToken) > 0;
        }
        finally
        {
            request.Body.Position = originalPosition;
        }
    }

    private async Task InvokeLegacyAsync(HttpContext context, string key)
    {
        var bodyHash = await ComputeRawBodyHashAsync(context.Request);
        var database = _redis.GetDatabase();
        var redisKey = (RedisKey)$"{_options.ServicePrefix}:idem:{key}";
        var existing = await database.StringGetAsync(redisKey);

        if (existing.HasValue)
        {
            LegacyStoredEntry? cached = null;
            try
            {
                cached = JsonSerializer.Deserialize<LegacyStoredEntry>(existing!, JsonOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Ignoring an invalid legacy idempotency record.");
            }

            if (cached is not null
                && string.Equals(cached.BodyHash, bodyHash, StringComparison.Ordinal)
                && cached.Body is not null)
            {
                context.Response.StatusCode = cached.StatusCode;
                await context.Response.Body.WriteAsync(Convert.FromBase64String(cached.Body));
                return;
            }

            await WriteErrorAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                MismatchErrorCode,
                "The idempotency key was reused with a different request body.");
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var responseBytes = buffer.ToArray();
        if (context.Response.StatusCode < StatusCodes.Status500InternalServerError)
        {
            var completed = new LegacyStoredEntry(
                context.Response.StatusCode,
                Convert.ToBase64String(responseBytes),
                bodyHash);
            var payload = JsonSerializer.Serialize(completed, JsonOptions);
            await database.StringSetAsync(
                redisKey,
                payload,
                TimeSpan.FromSeconds(CompletedTtlSeconds),
                When.NotExists,
                CommandFlags.None);
        }

        await context.Response.Body.WriteAsync(responseBytes);
    }

    private async Task ExecuteReservedAsync(
        HttpContext context,
        IDatabase database,
        RedisKey redisKey,
        string reservationPayload,
        string reservationToken,
        string fingerprint)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            await ReleaseReservationAsync(database, redisKey, reservationToken);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var responseBytes = buffer.ToArray();
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await ReleaseReservationAsync(database, redisKey, reservationToken);
            await context.Response.Body.WriteAsync(responseBytes);
            return;
        }

        var completed = new StoredEntry(
            "completed",
            fingerprint,
            null,
            context.Response.StatusCode,
            Convert.ToBase64String(responseBytes));
        var completedPayload = JsonSerializer.Serialize(completed, JsonOptions);

        var finalizeTask = database.ScriptEvaluateAsync(
            CompleteReservationScript,
            new[] { redisKey },
            new RedisValue[] { fingerprint, reservationToken, completedPayload, CompletedTtlSeconds });
        var finalizeResult = finalizeTask is null ? null : await finalizeTask;

        var finalized = finalizeResult is not null && (long)finalizeResult == 1;
        if (!finalized
            && !await TryFinalizeWithoutScriptAsync(
                database,
                redisKey,
                reservationPayload,
                completedPayload))
        {
            _logger.LogWarning("Idempotency reservation was not owned during response finalization.");
        }

        await context.Response.Body.WriteAsync(responseBytes);
    }

    private async Task HandleExistingAsync(
        HttpContext context,
        IDatabase database,
        RedisKey redisKey,
        string fingerprint)
    {
        var payload = await database.StringGetAsync(redisKey);
        StoredEntry? entry = null;
        LegacyStoredEntry? legacyEntry = null;

        if (payload.HasValue)
        {
            try
            {
                using var document = JsonDocument.Parse(payload.ToString());
                if (IsLegacyEntry(document.RootElement))
                {
                    legacyEntry = JsonSerializer.Deserialize<LegacyStoredEntry>(payload!, JsonOptions);
                }
                else
                {
                    entry = JsonSerializer.Deserialize<StoredEntry>(payload!, JsonOptions);
                }
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Ignoring an invalid idempotency record.");
            }
        }

        if (legacyEntry is not null)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                MismatchErrorCode,
                "The idempotency key was reused for a different request.");
            return;
        }

        if (entry is null)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status409Conflict,
                PendingErrorCode,
                "The idempotent request is still being processed.");
            return;
        }

        if (!string.Equals(entry.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                MismatchErrorCode,
                "The idempotency key was reused for a different request.");
            return;
        }

        if (string.Equals(entry.State, "pending", StringComparison.Ordinal))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status409Conflict,
                PendingErrorCode,
                "The idempotent request is still being processed.");
            return;
        }

        if (string.Equals(entry.State, "completed", StringComparison.Ordinal)
            && entry.StatusCode is not null
            && entry.Body is not null)
        {
            context.Response.StatusCode = entry.StatusCode.Value;
            await context.Response.Body.WriteAsync(Convert.FromBase64String(entry.Body));
            return;
        }

        await WriteErrorAsync(
            context,
            StatusCodes.Status409Conflict,
            PendingErrorCode,
            "The idempotent request is still being processed.");
    }

    private static bool IsLegacyEntry(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && !root.TryGetProperty("state", out _)
            && !root.TryGetProperty("requestFingerprint", out _)
            && root.TryGetProperty("statusCode", out var statusCode)
            && statusCode.ValueKind == JsonValueKind.Number
            && root.TryGetProperty("body", out var body)
            && body.ValueKind == JsonValueKind.String
            && root.TryGetProperty("bodyHash", out var bodyHash)
            && bodyHash.ValueKind == JsonValueKind.String;
    }

    private static async Task<string> ComputeRawBodyHashAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory);
        request.Body.Position = 0;

        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }

    private static bool TryReadHeaderValue(HttpContext context, out string key)
    {
        key = string.Empty;
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return false;
        }

        key = values.ToString();
        return true;
    }

    private static bool TryNormalizeUuidV4(string candidate, out string normalized)
    {
        normalized = string.Empty;
        if (!Guid.TryParseExact(candidate, "D", out var parsed) || candidate.Length != 36)
        {
            return false;
        }

        normalized = parsed.ToString("D");
        var variant = char.ToLowerInvariant(normalized[19]);
        if (normalized[14] != '4' || variant is not ('8' or '9' or 'a' or 'b'))
        {
            return false;
        }

        return true;
    }

    private static async Task ReleaseReservationAsync(
        IDatabase database,
        RedisKey redisKey,
        string reservationToken)
    {
        var releaseTask = database.ScriptEvaluateAsync(
            ReleaseReservationScript,
            new[] { redisKey },
            new RedisValue[] { reservationToken });
        if (releaseTask is not null)
        {
            await releaseTask;
        }
    }

    private static async Task<bool> TryFinalizeWithoutScriptAsync(
        IDatabase database,
        RedisKey redisKey,
        string reservationPayload,
        string completedPayload)
    {
        // Compatibility fallback for restricted Redis adapters/test doubles that do not execute Lua.
        // The Redis transaction condition and write execute atomically, so ownership cannot change
        // between the comparison and finalization.
        var transaction = database.CreateTransaction();
        if (transaction is null)
        {
            return false;
        }

        transaction.AddCondition(Condition.StringEqual(redisKey, reservationPayload));
        _ = transaction.StringSetAsync(
            redisKey,
            completedPayload,
            TimeSpan.FromSeconds(CompletedTtlSeconds),
            When.Always,
            CommandFlags.None);
        return await transaction.ExecuteAsync(CommandFlags.None);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        var envelope = ApiResponse.Failure(
            statusCode,
            new ApiError { Code = code, Message = message },
            ApiMeta.Create(GetTraceId(context)));

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload));
    }

    private static string GetTraceId(HttpContext context)
    {
        if (context.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            && id is string value
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return context.TraceIdentifier ?? string.Empty;
    }

    private sealed record StoredEntry(
        string State,
        string RequestFingerprint,
        string? ReservationToken,
        int? StatusCode,
        string? Body)
    {
        // Retained in the Redis JSON during migration so existing consumers that inspect
        // the old record shape do not break. The value is now the complete request fingerprint.
        [JsonPropertyName("bodyHash")]
        public string LegacyBodyHash => RequestFingerprint;
    }

    private sealed record LegacyStoredEntry(int StatusCode, string Body, string BodyHash);
}
