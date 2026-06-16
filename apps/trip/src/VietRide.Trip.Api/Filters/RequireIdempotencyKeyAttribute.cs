using Microsoft.AspNetCore.Mvc.Filters;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var values)
            || values.Count == 0
            || values.All(string.IsNullOrWhiteSpace))
        {
            throw new ValidationException(
                "Idempotency-Key header is required.",
                [new ValidationError(HeaderName, "Idempotency-Key header is required.")]);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
