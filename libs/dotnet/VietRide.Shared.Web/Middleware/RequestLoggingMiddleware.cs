using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace VietRide.Shared.Web.Middleware;

/// Per-request structured log + X-Request-Id propagation.
/// Per BACKEND_SOURCE_OF_TRUTH 5.3 (X-Request-Id forwarded through services).
public sealed class RequestLoggingMiddleware
{
    public const string RequestIdHeader = "X-Request-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var requestId = ctx.Request.Headers.TryGetValue(RequestIdHeader, out var value)
            && !string.IsNullOrWhiteSpace(value.ToString())
                ? value.ToString()
                : Guid.NewGuid().ToString("N");

        ctx.Response.Headers[RequestIdHeader] = requestId;
        ctx.Items[RequestIdHeader] = requestId;

        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("TraceId", requestId))
        using (LogContext.PushProperty("RequestPath", ctx.Request.Path.ToString()))
        using (LogContext.PushProperty("RequestMethod", ctx.Request.Method))
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _next(ctx);
            }
            finally
            {
                stopwatch.Stop();
                if (ctx.Response.StatusCode < 400 && ctx.Request.Path == "/health")
                {
                    _logger.LogDebug(
                        "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                        ctx.Request.Method,
                        ctx.Request.Path,
                        ctx.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                        ctx.Request.Method,
                        ctx.Request.Path,
                        ctx.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        }
    }
}
