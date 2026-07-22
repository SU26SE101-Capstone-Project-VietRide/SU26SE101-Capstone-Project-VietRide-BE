using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Swagger;

namespace VietRide.Testing;

public static partial class IdempotencyOpenApiContractAssertions
{
    private static readonly HashSet<string> MutationMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PATCH", "PUT", "DELETE" };

    public static async Task AssertMatchesRuntimeMetadataAsync(
        HttpClient client,
        IServiceProvider services)
    {
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        var options = services.GetRequiredService<IdempotencyOptions>();
        var endpointSource = services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in endpointSource.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(MutationMethods.Contains))
            {
                var path = NormalizeOpenApiPath(endpoint.RoutePattern.RawText ?? string.Empty);
                paths.TryGetProperty(path, out var pathItem).Should().BeTrue(
                    $"{method} {path} is a routed controller mutation and must exist in OpenAPI");
                pathItem.TryGetProperty(method.ToLowerInvariant(), out var operation).Should().BeTrue(
                    $"{method} {path} must expose an OpenAPI operation");

                var skipped = endpoint.Metadata.GetMetadata<SkipIdempotencyAttribute>() is not null;
                var explicitlyRequired = endpoint.Metadata
                    .GetOrderedMetadata<IIdempotencyPolicyMetadata>()
                    .Any(policy => policy.IsRequired);
                var required = !skipped && (options.RequireAllMutations || explicitlyRequired);

                AssertOperation(operation, required, method, path);
            }
        }
    }

    private static void AssertOperation(JsonElement operation, bool required, string method, string path)
    {
        var context = $"{method} {path}";
        var idempotencyHeaders = operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Where(IsIdempotencyHeader).ToArray()
            : [];
        var hasExtension = operation.TryGetProperty(
            IdempotencyOperationFilter.ExtensionName,
            out var extension);

        if (!required)
        {
            idempotencyHeaders.Should().BeEmpty($"{context} is not idempotency-required");
            hasExtension.Should().BeFalse($"{context} is not idempotency-required");
            return;
        }

        var header = idempotencyHeaders.Should().ContainSingle(
            $"{context} must document exactly one Idempotency-Key header").Subject;
        header.GetProperty("required").GetBoolean().Should().BeTrue();
        var schema = header.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("format").GetString().Should().Be("uuid");

        hasExtension.Should().BeTrue($"{context} must expose machine-readable idempotency metadata");
        extension.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["required", "keyFormat", "ttlSeconds"]);
        extension.GetProperty("required").GetBoolean().Should().BeTrue();
        extension.GetProperty("keyFormat").GetString().Should().Be("uuid-v4");
        extension.GetProperty("ttlSeconds").GetInt32().Should().Be(86_400);
    }

    private static bool IsIdempotencyHeader(JsonElement parameter)
        => parameter.TryGetProperty("in", out var location)
            && location.GetString() == "header"
            && parameter.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "Idempotency-Key", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOpenApiPath(string route)
    {
        var normalized = RouteConstraintRegex().Replace(route, "{$1}");
        return "/" + normalized.Trim('/');
    }

    [GeneratedRegex(@"{([^}:?]+)(?::[^}?]+)?\??}")]
    private static partial Regex RouteConstraintRegex();
}
