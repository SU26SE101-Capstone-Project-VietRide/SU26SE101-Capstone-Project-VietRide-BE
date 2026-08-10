using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Serialization;

namespace VietRide.Shared.Web.Filters;

/// <summary>
/// Auto-wraps every successful <see cref="ObjectResult"/> into <see cref="ApiResponse{T}"/>
/// (ADR 0004 Rule 5 — controllers stay thin, the filter produces the envelope centrally).
/// <list type="bullet">
///   <item>204 No Content → empty body (no envelope).</item>
///   <item>Already-wrapped <see cref="ApiResponse{T}"/> → not double-wrapped.</item>
///   <item>Paths matching <c>/.well-known/*</c>, <c>/v1/.well-known/jwks.json</c>,
///   or <c>/health*</c> → skipped (exempt per resolved Q-v7.5.1 / Q-v7.5.4).</item>
///   <item>Successful <c>/internal/*</c> responses → raw payload; errors still use the ADR 0004 error envelope.</item>
/// </list>
/// Registered as <see cref="IAlwaysRunResultFilter"/> so it runs even when a short-circuit filter
/// (e.g. an authorization filter) produces a result directly.
/// </summary>
public sealed class ApiResponseResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // Exempt standard endpoints that must NOT be wrapped.
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/v1/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (context.Result is not ObjectResult { StatusCode: not 204 } objectResult)
            return;

        var value = objectResult.Value;
        var valueType = value?.GetType() ?? typeof(object);

        // Do not double-wrap if the handler already returned ApiResponse / ApiResponse<T>.
        if (valueType == typeof(ApiResponse)
            || (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ApiResponse<>)))
        {
            return;
        }

        var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
        if (statusCode == 204 || statusCode < 200 || statusCode >= 300)
        {
            return;
        }

        if (IsInternalPath(path))
        {
            return;
        }

        var traceId = GetTraceId(context);
        var meta = ApiTimestampPresentation.CreateMeta(context.HttpContext, traceId);
        var wrapped = Wrap(value, valueType, statusCode, meta);

        context.Result = new ObjectResult(wrapped) { StatusCode = statusCode };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    // ------------------------------------------------------------------
    // Helpers — use reflection to call the typed static factory method
    // so the envelope carries ApiResponse<TActual> not ApiResponse<object>.
    // ------------------------------------------------------------------

    private static object Wrap(object? value, Type valueType, int statusCode, ApiMeta meta)
    {
        var generic = typeof(ApiResponse<>).MakeGenericType(valueType);
        var method = generic.GetMethod(nameof(ApiResponse<object>.SuccessResponse),
            [typeof(int), valueType, typeof(ApiMeta), typeof(string)])!;
        return method.Invoke(null, [statusCode, value, meta, null])!;
    }

    private static bool IsInternalPath(string path)
        => path.Equals("/internal", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/internal/", StringComparison.OrdinalIgnoreCase);

    private static string GetTraceId(ResultExecutingContext context)
    {
        if (context.HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var id)
            && id is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return context.HttpContext.Request.Headers
            .TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var h)
            ? h.ToString()
            : string.Empty;
    }
}
