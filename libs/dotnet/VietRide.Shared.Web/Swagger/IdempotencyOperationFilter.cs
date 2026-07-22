using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Middleware;

namespace VietRide.Shared.Web.Swagger;

/// <summary>Adds the runtime idempotency contract to generated OpenAPI operations.</summary>
public sealed class IdempotencyOperationFilter : IOperationFilter
{
    public const string ExtensionName = "x-vietride-idempotency";

    private const string UuidFormat = "uuid";
    private const string UuidV4Format = "uuid-v4";
    private const int TtlSeconds = 86400;
    private readonly IdempotencyOptions _options;

    public IdempotencyOperationFilter(IdempotencyOptions options)
    {
        _options = options;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<SkipIdempotencyAttribute>().Any())
        {
            if (operation.Parameters is not null)
            {
                foreach (var parameter in operation.Parameters.Where(IsIdempotencyHeader).ToArray())
                {
                    operation.Parameters.Remove(parameter);
                }
            }

            operation.Extensions.Remove(ExtensionName);
            return;
        }

        var explicitlyRequired = metadata
            .OfType<IIdempotencyPolicyMetadata>()
            .Any(policy => policy.IsRequired);
        var requiredForMutation = _options.RequireAllMutations
            && IsMutation(context.ApiDescription.HttpMethod);
        if (!explicitlyRequired && !requiredForMutation)
        {
            return;
        }

        operation.Parameters ??= [];
        var header = operation.Parameters.FirstOrDefault(IsIdempotencyHeader);
        if (header is null)
        {
            header = new OpenApiParameter
            {
                Name = IdempotencyMiddleware.IdempotencyKeyHeader,
                In = ParameterLocation.Header,
            };
            operation.Parameters.Add(header);
        }

        header.Required = true;
        header.Description = "UUID v4. Reuse the same key only when retrying the same request.";
        header.Schema = new OpenApiSchema
        {
            Type = "string",
            Format = UuidFormat,
        };
        operation.Extensions[ExtensionName] = new OpenApiObject
        {
            ["required"] = new OpenApiBoolean(true),
            ["keyFormat"] = new OpenApiString(UuidV4Format),
            ["ttlSeconds"] = new OpenApiInteger(TtlSeconds),
        };
    }

    private static bool IsMutation(string? method)
        => method is not null
            && (HttpMethods.IsPost(method)
                || HttpMethods.IsPatch(method)
                || HttpMethods.IsPut(method)
                || HttpMethods.IsDelete(method));

    private static bool IsIdempotencyHeader(OpenApiParameter parameter)
        => parameter.In == ParameterLocation.Header
            && string.Equals(
                parameter.Name,
                IdempotencyMiddleware.IdempotencyKeyHeader,
                StringComparison.OrdinalIgnoreCase);
}
