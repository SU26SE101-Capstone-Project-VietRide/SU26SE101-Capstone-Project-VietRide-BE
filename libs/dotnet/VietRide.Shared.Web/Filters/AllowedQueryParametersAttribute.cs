using Microsoft.AspNetCore.Mvc.Filters;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Shared.Web.Filters;

/// <summary>
/// Rejects query-string keys that are not part of an endpoint's documented contract.
/// Matching is case-insensitive because ASP.NET Core query binding is case-insensitive.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowedQueryParametersAttribute : ActionFilterAttribute
{
    private readonly HashSet<string> allowedParameters;

    public AllowedQueryParametersAttribute(params string[] allowedParameters)
    {
        this.allowedParameters = new HashSet<string>(allowedParameters, StringComparer.OrdinalIgnoreCase);
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var errors = context.HttpContext.Request.Query.Keys
            .Where(key => !allowedParameters.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => new ValidationError(key, $"Query parameter '{key}' is not supported."))
            .ToArray();

        if (errors.Length > 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "One or more query parameters are not supported.",
                errors);
        }
    }
}
