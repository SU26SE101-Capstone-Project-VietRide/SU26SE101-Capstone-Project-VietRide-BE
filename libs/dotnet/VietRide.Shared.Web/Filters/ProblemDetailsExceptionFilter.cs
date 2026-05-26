using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Exceptions;

namespace VietRide.Shared.Web.Filters;

/// Global exception → application/problem+json (RFC 7807).
/// Per BACKEND_SOURCE_OF_TRUTH 5.5.
public sealed class ProblemDetailsExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ProblemDetailsExceptionFilter> _logger;

    public ProblemDetailsExceptionFilter(ILogger<ProblemDetailsExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var (status, errorCode, detail, errors) = Map(context.Exception);

        var problem = new ProblemDetails
        {
            Type = $"https://vietride.app/errors/{errorCode}",
            Title = HumanTitle(errorCode),
            Status = status,
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        };
        problem.Extensions["errorCode"] = errorCode;
        if (errors is not null) problem.Extensions["errors"] = errors;

        if (status >= 500)
            _logger.LogError(context.Exception, "Unhandled exception → {ErrorCode}", errorCode);
        else
            _logger.LogWarning("Handled {ErrorCode}: {Message}", errorCode, context.Exception.Message);

        context.Result = new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }

    private static (int Status, string ErrorCode, string Detail, object? Errors) Map(Exception ex) => ex switch
    {
        ValidationException v => (422, "VALIDATION_ERROR", v.Message, v.Errors),
        NotFoundException n => (404, "NOT_FOUND", n.Message, null),
        ConflictException c => (409, c.ErrorCode, c.Message, null),
        ForbiddenException f => (403, f.ErrorCode, f.Message, null),
        DomainException d => (422, d.ErrorCode, d.Message, null),
        UnauthorizedAccessException => (401, "UNAUTHORIZED", "Authentication required", null),
        _ => (500, "INTERNAL_ERROR", "An unexpected error occurred", null),
    };

    private static string HumanTitle(string errorCode) =>
        errorCode.Replace('_', ' ').ToLowerInvariant() is var s
            ? char.ToUpperInvariant(s[0]) + s[1..]
            : errorCode;
}
