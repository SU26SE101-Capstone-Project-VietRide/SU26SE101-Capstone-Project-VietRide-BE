using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Middleware;
using VietRide.Shared.Web.Serialization;

namespace VietRide.Identity.Api.Filters;

public sealed class SubscriptionUniqueConstraintExceptionFilter : IExceptionFilter
{
    private static readonly IReadOnlyDictionary<string, (string Code, string Message)> Mappings =
        new Dictionary<string, (string Code, string Message)>(StringComparer.Ordinal)
        {
            ["uq_subscription_upgrade_attempts_active_subscription"] = (
                "SUBSCRIPTION_UPGRADE_ALREADY_ACTIVE",
                "An active subscription upgrade already exists."),
            ["uq_subscription_custom_requests_pending_operator"] = (
                "CUSTOM_REQUEST_ALREADY_PENDING",
                "The operator already has a pending custom subscription request."),
        };

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                } postgresException,
            }
            || postgresException.ConstraintName is null
            || !Mappings.TryGetValue(postgresException.ConstraintName, out var mapping))
        {
            return;
        }

        var traceId = context.HttpContext.Items.TryGetValue(RequestLoggingMiddleware.RequestIdHeader, out var requestId)
            ? requestId?.ToString() ?? string.Empty
            : context.HttpContext.TraceIdentifier;
        var response = ApiResponse.Failure(
            StatusCodes.Status409Conflict,
            new ApiError { Code = mapping.Code, Message = mapping.Message },
            ApiTimestampPresentation.CreateMeta(context.HttpContext, traceId));
        context.Result = new ObjectResult(response) { StatusCode = StatusCodes.Status409Conflict };
        context.ExceptionHandled = true;
    }
}
