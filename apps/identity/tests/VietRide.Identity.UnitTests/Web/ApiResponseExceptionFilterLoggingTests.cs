using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Filters;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Identity.UnitTests.Web;

public sealed class ApiResponseExceptionFilterLoggingTests
{
    [Fact]
    public void OnException_ValidationError_LogsInformationWithoutException()
    {
        var logger = new CapturingLogger<ApiResponseExceptionFilter>();
        var filter = new ApiResponseExceptionFilter(logger);
        var context = CreateContext(new ValidationException(
            "Validation failed.",
            [new ValidationError("field", "Invalid value.")]));

        filter.OnException(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.True(context.ExceptionHandled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ((ObjectResult)context.Result!).StatusCode);
    }

    [Fact]
    public void OnException_ForbiddenError_LogsWarningWithoutException()
    {
        var logger = new CapturingLogger<ApiResponseExceptionFilter>();
        var filter = new ApiResponseExceptionFilter(logger);
        var context = CreateContext(new ForbiddenException("FORBIDDEN", "Access denied."));

        filter.OnException(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)context.Result!).StatusCode);
    }

    [Fact]
    public void OnException_UnhandledError_LogsExactlyOneErrorWithOriginalException()
    {
        var logger = new CapturingLogger<ApiResponseExceptionFilter>();
        var filter = new ApiResponseExceptionFilter(logger);
        var exception = new InvalidOperationException("Unexpected failure.");
        var context = CreateContext(exception);

        filter.OnException(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((ObjectResult)context.Result!).StatusCode);
    }

    private static ExceptionContext CreateContext(Exception exception)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/v1/test";
        httpContext.Items[RequestLoggingMiddleware.RequestIdHeader] = "trace-test";
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        return new ExceptionContext(actionContext, []) { Exception = exception };
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception);
}
