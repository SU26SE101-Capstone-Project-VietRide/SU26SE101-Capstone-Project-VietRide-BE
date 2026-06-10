using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.DependencyInjection;

namespace VietRide.Shared.Web.Middleware;

/// <summary>
/// PLACEHOLDER Redis-backed idempotency middleware for Sprint-3 reuse.
/// NOT wired into any running service pipeline yet — see
/// <see cref="DependencyInjection.IdempotencyServiceCollectionExtensions"/> for the opt-in helper.
///
/// Behaviour (BSOT §5.6 + §9.8 + §5.9):
///   • Only POST and PATCH are processed; everything else passes through untouched.
///   • Missing/empty Idempotency-Key header → pass-through no-op.
///   • First request for a key → proceed, capture response, SETNX-store {statusCode, body, bodyHash} (TTL 24h).
///   • Subsequent request, same body hash → replay cached response verbatim (downstream NOT invoked).
///   • Subsequent request, different body hash → HTTP 422 IDEMPOTENCY_KEY_MISMATCH (downstream NOT invoked).
/// </summary>
public sealed class IdempotencyMiddleware
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>BSOT error code (already registered): mismatched body for a reused idempotency key.</summary>
    public const string MismatchErrorCode = "IDEMPOTENCY_KEY_MISMATCH";

    private static readonly TimeSpan KeyTtl = TimeSpan.FromSeconds(86400); // 24h

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public async Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPatch(method))
        {
            await _next(ctx);
            return;
        }

        // 1. Idempotency-Key header — missing/empty → pass-through no-op.
        if (!ctx.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var keyValues)
            || string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            await _next(ctx);
            return;
        }

        var key = keyValues.ToString();

        // 2. Buffer + hash the request body so downstream can still read it.
        var bodyHash = await ComputeBodyHashAsync(ctx.Request);

        var db = _redis.GetDatabase();
        var redisKey = $"{_options.ServicePrefix}:idem:{key}";

        // 3/4. Existing key → replay or mismatch.
        var existing = await db.StringGetAsync(redisKey);
        if (existing.HasValue)
        {
            var cached = JsonSerializer.Deserialize<CachedResponse>(existing!, JsonOptions);
            if (cached is not null && string.Equals(cached.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                await WriteCachedAsync(ctx, cached);
            }
            else
            {
                await WriteMismatchAsync(ctx);
            }

            return;
        }

        // 5. First request for this key → capture response, then SETNX-store it.
        var originalBody = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        try
        {
            await _next(ctx);
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }

        buffer.Position = 0;
        var responseBytes = buffer.ToArray();

        var entry = new CachedResponse(
            ctx.Response.StatusCode,
            Convert.ToBase64String(responseBytes),
            bodyHash);

        var payload = JsonSerializer.Serialize(entry, JsonOptions);
        await db.StringSetAsync(redisKey, payload, KeyTtl, When.NotExists, CommandFlags.None);

        // Copy the captured body back to the real response stream.
        await ctx.Response.Body.WriteAsync(responseBytes);
    }

    private static async Task<string> ComputeBodyHashAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        request.Body.Position = 0; // rewind so downstream reads from the start

        var hash = SHA256.HashData(ms.ToArray());
        return Convert.ToHexString(hash);
    }

    private static async Task WriteCachedAsync(HttpContext ctx, CachedResponse cached)
    {
        ctx.Response.StatusCode = cached.StatusCode;
        var bytes = Convert.FromBase64String(cached.Body);
        await ctx.Response.Body.WriteAsync(bytes);
    }

    private async Task WriteMismatchAsync(HttpContext ctx)
    {
        var traceId = GetTraceId(ctx);
        var envelope = ApiResponse.Failure(
            422,
            new ApiError
            {
                Code = MismatchErrorCode,
                Message = "The idempotency key was reused with a different request body.",
            },
            ApiMeta.Create(traceId));

        ctx.Response.StatusCode = 422;
        ctx.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(payload));
    }

    private static string GetTraceId(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            && id is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return ctx.TraceIdentifier ?? string.Empty;
    }

    /// <summary>Cached response stored at the Redis key.</summary>
    /// <param name="StatusCode">Captured HTTP status code.</param>
    /// <param name="Body">Base64-encoded response body bytes.</param>
    /// <param name="BodyHash">Hex SHA-256 of the originating request body.</param>
    internal sealed record CachedResponse(int StatusCode, string Body, string BodyHash);
}
