using Microsoft.AspNetCore.Mvc.Filters;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Parcel.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IActionFilter, IIdempotencyPolicyMetadata
{
    public const string HeaderName = "Idempotency-Key";
    public bool IsRequired => true;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var values)
            || values.Count == 0
            || values.All(string.IsNullOrWhiteSpace))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.",
                [new ValidationError(HeaderName, "Idempotency-Key header is required.")]);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
