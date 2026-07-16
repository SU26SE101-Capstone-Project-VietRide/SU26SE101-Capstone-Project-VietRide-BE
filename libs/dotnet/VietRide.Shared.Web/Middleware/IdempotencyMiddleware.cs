using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Shared.Web.Middleware;

/// <summary>
/// Redis-backed idempotency middleware for HTTP mutations. Endpoints opt in to mandatory UUID-v4
/// validation with <see cref="RequireIdempotencyAttribute"/>; other mutations remain pass-through
/// when the header is absent.
/// </summary>
public sealed class IdempotencyMiddleware
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string RequiredErrorCode = "IDEMPOTENCY_KEY_REQUIRED";
    public const string MismatchErrorCode = "IDEMPOTENCY_KEY_MISMATCH";
    public const string PendingErrorCode = "IDEMPOTENCY_REQUEST_PENDING";

    private const int ResponseTtlSeconds = 86400;
    private const int ProcessingTtlSeconds = 120;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string CompleteProcessingScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            return 0
        end
        local ok, entry = pcall(cjson.decode, current)
        if ok
            and entry.requestFingerprint == ARGV[1]
            and entry.ownerToken == ARGV[2] then
            redis.call('SET', KEYS[2], ARGV[3], 'EX', ARGV[4])
            redis.call('DEL', KEYS[1])
            return 1
        end
        return 0
        """;

    private const string ReleaseProcessingScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            return 0
        end
        local ok, entry = pcall(cjson.decode, current)
        if ok and entry.ownerToken == ARGV[1] then
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
        if (!IsMutation(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<RequireIdempotencyAttribute>();
        var requiresIdempotency = metadata is not null;
        if (!TryReadHeaderValue(context, out var key))
        {
            if (requiresIdempotency)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    RequiredErrorCode,
                    "Idempotency-Key header is required.");
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

        if (metadata is not null
            && !metadata.AllowRequestBody
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

        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var legacyKey = (RedisKey)$"{_options.ServicePrefix}:idem:{key}";
        var responseKey = (RedisKey)$"{_options.ServicePrefix}:idem:v2:response:{keyHash}";
        var processingKey = (RedisKey)$"{_options.ServicePrefix}:idem:v2:processing:{keyHash}";
        var database = _redis.GetDatabase();

        if (await database.KeyExistsAsync(legacyKey, CommandFlags.None))
        {
            await WriteMismatchAsync(context);
            return;
        }

        var fingerprint = await IdempotencyFingerprint.ComputeAsync(context);
        if (await TryReplayResponseAsync(context, database, responseKey, fingerprint))
        {
            return;
        }

        var ownerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var processingEntry = new ProcessingEntry(fingerprint, ownerToken);
        var processingPayload = JsonSerializer.Serialize(processingEntry, JsonOptions);
        if (!await TryAcquireProcessingAsync(database, processingKey, processingPayload))
        {
            await HandleProcessingConflictAsync(
                context,
                database,
                responseKey,
                processingKey,
                fingerprint,
                processingPayload,
                ownerToken);
            return;
        }

        await ExecuteOrReplayOwnedAsync(
            context,
            database,
            responseKey,
            processingKey,
            processingPayload,
            ownerToken,
            fingerprint);
    }

    private async Task HandleProcessingConflictAsync(
        HttpContext context,
        IDatabase database,
        RedisKey responseKey,
        RedisKey processingKey,
        string fingerprint,
        string processingPayload,
        string ownerToken)
    {
        if (await TryReplayResponseAsync(context, database, responseKey, fingerprint))
        {
            return;
        }

        var current = await database.StringGetAsync(processingKey, CommandFlags.None);
        if (current.HasValue)
        {
            ProcessingEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<ProcessingEntry>(current!, JsonOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Ignoring an invalid idempotency processing record.");
            }

            if (entry is not null
                && !string.Equals(entry.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                await WriteMismatchAsync(context);
                return;
            }

            await WritePendingAsync(context);
            return;
        }

        if (await TryReplayResponseAsync(context, database, responseKey, fingerprint))
        {
            return;
        }

        if (await TryAcquireProcessingAsync(database, processingKey, processingPayload))
        {
            await ExecuteOrReplayOwnedAsync(
                context,
                database,
                responseKey,
                processingKey,
                processingPayload,
                ownerToken,
                fingerprint);
            return;
        }

        if (await TryReplayResponseAsync(context, database, responseKey, fingerprint))
        {
            return;
        }

        var latest = await database.StringGetAsync(processingKey, CommandFlags.None);
        if (latest.HasValue)
        {
            try
            {
                var latestEntry = JsonSerializer.Deserialize<ProcessingEntry>(latest!, JsonOptions);
                if (latestEntry is not null
                    && !string.Equals(
                        latestEntry.RequestFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    await WriteMismatchAsync(context);
                    return;
                }
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Ignoring an invalid idempotency processing record.");
            }
        }

        await WritePendingAsync(context);
    }

    private async Task ExecuteOrReplayOwnedAsync(
        HttpContext context,
        IDatabase database,
        RedisKey responseKey,
        RedisKey processingKey,
        string processingPayload,
        string ownerToken,
        string fingerprint)
    {
        if (await TryReplayResponseAsync(context, database, responseKey, fingerprint))
        {
            await ReleaseProcessingAsync(database, processingKey, processingPayload, ownerToken);
            return;
        }

        await ExecuteOwnedAsync(
            context,
            database,
            responseKey,
            processingKey,
            processingPayload,
            ownerToken,
            fingerprint);
    }

    private async Task ExecuteOwnedAsync(
        HttpContext context,
        IDatabase database,
        RedisKey responseKey,
        RedisKey processingKey,
        string processingPayload,
        string ownerToken,
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
            await ReleaseProcessingAsync(database, processingKey, processingPayload, ownerToken);
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var responseBytes = buffer.ToArray();
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await ReleaseProcessingAsync(database, processingKey, processingPayload, ownerToken);
            await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted);
            return;
        }

        var responseEntry = new ResponseEntry(
            fingerprint,
            context.Response.StatusCode,
            context.Response.ContentType,
            Convert.ToBase64String(responseBytes));
        var responsePayload = JsonSerializer.Serialize(responseEntry, JsonOptions);
        var completed = await CompleteProcessingAsync(
            database,
            processingKey,
            responseKey,
            processingPayload,
            ownerToken,
            fingerprint,
            responsePayload);

        if (!completed)
        {
            _logger.LogWarning("Idempotency processing lock was not owned during response finalization.");
        }

        await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted);
    }

    private static async Task<bool> TryReplayResponseAsync(
        HttpContext context,
        IDatabase database,
        RedisKey responseKey,
        string fingerprint)
    {
        var payload = await database.StringGetAsync(responseKey, CommandFlags.None);
        if (!payload.HasValue)
        {
            return false;
        }

        ResponseEntry? entry = null;
        try
        {
            entry = JsonSerializer.Deserialize<ResponseEntry>(payload!, JsonOptions);
        }
        catch (JsonException)
        {
            // A malformed v2 entry is never trusted for replay.
        }

        byte[] responseBytes;
        try
        {
            if (entry is null
                || entry.StatusCode is < 100 or > 599
                || !string.Equals(entry.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                await WriteMismatchAsync(context);
                return true;
            }

            responseBytes = Convert.FromBase64String(entry.Body);
        }
        catch (FormatException)
        {
            await WriteMismatchAsync(context);
            return true;
        }

        context.Response.StatusCode = entry.StatusCode;
        if (!string.IsNullOrWhiteSpace(entry.ContentType))
        {
            context.Response.ContentType = entry.ContentType;
        }

        await context.Response.Body.WriteAsync(
            responseBytes,
            context.RequestAborted);
        return true;
    }

    private static Task<bool> TryAcquireProcessingAsync(
        IDatabase database,
        RedisKey processingKey,
        string processingPayload)
        => database.StringSetAsync(
            processingKey,
            processingPayload,
            TimeSpan.FromSeconds(ProcessingTtlSeconds),
            When.NotExists,
            CommandFlags.None);

    private static async Task<bool> CompleteProcessingAsync(
        IDatabase database,
        RedisKey processingKey,
        RedisKey responseKey,
        string processingPayload,
        string ownerToken,
        string fingerprint,
        string responsePayload)
    {
        var scriptTask = database.ScriptEvaluateAsync(
            CompleteProcessingScript,
            new[] { processingKey, responseKey },
            new RedisValue[] { fingerprint, ownerToken, responsePayload, ResponseTtlSeconds });
        if (scriptTask is not null)
        {
            var result = await scriptTask;
            if (result is not null && !result.IsNull)
            {
                return (long)result == 1;
            }
        }

        var transaction = database.CreateTransaction();
        if (transaction is null)
        {
            return false;
        }

        transaction.AddCondition(Condition.StringEqual(processingKey, processingPayload));
        _ = transaction.StringSetAsync(
            responseKey,
            responsePayload,
            TimeSpan.FromSeconds(ResponseTtlSeconds),
            When.Always,
            CommandFlags.None);
        _ = transaction.KeyDeleteAsync(processingKey, CommandFlags.None);
        return await transaction.ExecuteAsync(CommandFlags.None);
    }

    private static async Task ReleaseProcessingAsync(
        IDatabase database,
        RedisKey processingKey,
        string processingPayload,
        string ownerToken)
    {
        var scriptTask = database.ScriptEvaluateAsync(
            ReleaseProcessingScript,
            new[] { processingKey },
            new RedisValue[] { ownerToken });
        if (scriptTask is not null)
        {
            await scriptTask;
            return;
        }

        var transaction = database.CreateTransaction();
        if (transaction is null)
        {
            return;
        }

        transaction.AddCondition(Condition.StringEqual(processingKey, processingPayload));
        _ = transaction.KeyDeleteAsync(processingKey, CommandFlags.None);
        await transaction.ExecuteAsync(CommandFlags.None);
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

    private static bool IsMutation(string method)
        => HttpMethods.IsPost(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsDelete(method);

    private static bool TryReadHeaderValue(HttpContext context, out string key)
    {
        key = string.Empty;
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]))
        {
            return false;
        }

        key = values[0]!.Trim();
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
        return normalized[14] == '4' && variant is '8' or '9' or 'a' or 'b';
    }

    private static Task WriteMismatchAsync(HttpContext context)
        => WriteErrorAsync(
            context,
            StatusCodes.Status422UnprocessableEntity,
            MismatchErrorCode,
            "The idempotency key was reused for a different request.");

    private static Task WritePendingAsync(HttpContext context)
        => WriteErrorAsync(
            context,
            StatusCodes.Status409Conflict,
            PendingErrorCode,
            "The idempotent request is still being processed.");

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
        await context.Response.Body.WriteAsync(
            Encoding.UTF8.GetBytes(payload),
            context.RequestAborted);
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

    private sealed record ProcessingEntry(string RequestFingerprint, string OwnerToken);

    private sealed record ResponseEntry(
        string RequestFingerprint,
        int StatusCode,
        string? ContentType,
        string Body);
}
