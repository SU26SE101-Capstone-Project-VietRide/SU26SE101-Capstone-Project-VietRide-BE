using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Shared.Web.Filters;

/// <summary>
/// Global exception → <see cref="ApiResponse"/> error envelope (ADR 0004 Rule 5).
/// Replaces <c>ProblemDetailsExceptionFilter</c> — the old RFC 7807 filter is removed.
/// <para>
/// Each exception arm preserves the same HTTP status code + <c>error.code</c> (BSOT §5.9 registry)
/// as before; only the transport shape changes from RFC 7807 ProblemDetails to the unified envelope.
/// </para>
/// <para>
/// Structured log fields: <c>UserId</c> (from <c>sub</c> claim), <c>Path</c>, <c>Method</c>,
/// <c>TraceId</c> — consistent with ADR 0004 Rule 5 logging requirements.
/// </para>
/// </summary>
public sealed class ApiResponseExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiResponseExceptionFilter> _logger;

    public ApiResponseExceptionFilter(ILogger<ApiResponseExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var (statusCode, errorCode, message, fields) = Map(context.Exception);
        var traceId = GetTraceId(context);
        var meta = ApiMeta.Create(traceId);

        var error = new ApiError
        {
            Code = errorCode,
            Message = message,
            Fields = fields,
        };

        var envelope = ApiResponse.Failure(statusCode, error, meta);

        if (statusCode >= 500)
        {
            _logger.LogError(
                context.Exception,
                "Unhandled exception {ErrorCode} | UserId={UserId} | {Method} {Path} | TraceId={TraceId}",
                errorCode,
                GetUserId(context),
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                traceId);
        }
        else
        {
            _logger.LogWarning(
                "Handled {ErrorCode}: {ExMessage} | UserId={UserId} | {Method} {Path} | TraceId={TraceId}",
                errorCode,
                context.Exception.Message,
                GetUserId(context),
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                traceId);
        }

        context.Result = new ObjectResult(envelope) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }

    // ------------------------------------------------------------------
    // Exception → (statusCode, errorCode, message, fields?) mapping.
    // Preserves all arms from ProblemDetailsExceptionFilter.
    // ------------------------------------------------------------------

    private static (int StatusCode, string ErrorCode, string Message, IReadOnlyList<ApiFieldError>? Fields)
        Map(Exception ex) => ex switch
        {
            BadRequestException b
                => (400, b.ErrorCode, b.Message, null),

            ValidationException v
                => (422, "VALIDATION_ERROR", v.Message, MapValidationErrors(v.Errors)),

            CodedValidationException v
                => (422, v.ErrorCode, v.Message, MapValidationErrors(v.Errors)),

            CodedNotFoundException n
                => (404, n.ErrorCode, n.Message, null),

            NotFoundException n
                => (404, "RESOURCE_NOT_FOUND", n.Message, null),

            ConflictException c
                => (409, c.ErrorCode, c.Message, null),

            CodedConflictException c
                => (409, c.ErrorCode, c.Message, MapValidationErrors(c.Errors)),

            ForbiddenException f
                => (403, f.ErrorCode, f.Message, null),

            UnauthorizedException u
                => (401, u.ErrorCode, u.Message, null),

            TooManyRequestsException t
                => (429, t.ErrorCode, t.Message, null),

            DomainException { ErrorCode: "SUBSCRIPTION_EXPIRED" } d
                => (402, d.ErrorCode, d.Message, null),

            DomainException d
                => (422, d.ErrorCode, d.Message, null),

            UnauthorizedAccessException
                => (401, "UNAUTHORIZED", "Authentication required", null),

            _
                => (500, "INTERNAL_ERROR", "An unexpected error occurred", null),
        };

    private static IReadOnlyList<ApiFieldError>? MapValidationErrors(
        IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count == 0) return null;
        return errors.Select(e => new ApiFieldError(e.Field, e.Message)).ToList();
    }

    private static string GetTraceId(ExceptionContext context)
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

    private static string GetUserId(ExceptionContext context)
    {
        return context.HttpContext.User.FindFirst("sub")?.Value ?? "anonymous";
    }
}
