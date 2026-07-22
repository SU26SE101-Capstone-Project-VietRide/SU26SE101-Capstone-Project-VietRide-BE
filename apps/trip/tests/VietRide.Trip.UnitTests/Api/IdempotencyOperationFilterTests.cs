using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Idempotency;
using VietRide.Shared.Web.Swagger;

namespace VietRide.Trip.UnitTests.Api;

public sealed class IdempotencyOperationFilterTests
{
    [Fact]
    public void Apply_ExplicitRequirement_AddsHeaderAndExtension()
    {
        var operation = new OpenApiOperation();
        var filter = new IdempotencyOperationFilter(new IdempotencyOptions());

        filter.Apply(operation, CreateContext("POST", new IdempotencyOpenApiAttribute()));

        AssertIdempotencyDocumentation(operation);
    }

    [Fact]
    public void Apply_RequireAllMutation_AddsHeader()
    {
        var operation = new OpenApiOperation();
        var filter = new IdempotencyOperationFilter(new IdempotencyOptions
        {
            RequireAllMutations = true,
        });

        filter.Apply(operation, CreateContext("DELETE"));

        AssertIdempotencyDocumentation(operation);
    }

    [Fact]
    public void Apply_SkipMetadata_WinsOverRequireAll()
    {
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    In = ParameterLocation.Header,
                    Required = true,
                },
            ],
            Extensions =
            {
                [IdempotencyOperationFilter.ExtensionName] = new Microsoft.OpenApi.Any.OpenApiObject(),
            },
        };
        var filter = new IdempotencyOperationFilter(new IdempotencyOptions
        {
            RequireAllMutations = true,
        });

        filter.Apply(operation, CreateContext("POST", new SkipIdempotencyAttribute("Provider callback.")));

        operation.Parameters.Should().BeNullOrEmpty();
        operation.Extensions.Should().NotContainKey(IdempotencyOperationFilter.ExtensionName);
    }

    [Fact]
    public void Apply_Get_DoesNotAddHeader()
    {
        var operation = new OpenApiOperation();
        var filter = new IdempotencyOperationFilter(new IdempotencyOptions
        {
            RequireAllMutations = true,
        });

        filter.Apply(operation, CreateContext("GET"));

        operation.Parameters.Should().BeNullOrEmpty();
        operation.Extensions.Should().NotContainKey(IdempotencyOperationFilter.ExtensionName);
    }

    [Fact]
    public void Apply_ExistingHeader_NormalizesWithoutDuplicating()
    {
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "Idempotency-Key",
                    In = ParameterLocation.Header,
                    Required = false,
                },
            ],
        };
        var filter = new IdempotencyOperationFilter(new IdempotencyOptions());

        filter.Apply(operation, CreateContext("POST", new IdempotencyOpenApiAttribute()));

        operation.Parameters.Should().ContainSingle();
        AssertIdempotencyDocumentation(operation);
    }

    private static OperationFilterContext CreateContext(string httpMethod, params object[] metadata)
    {
        var actionDescriptor = new ControllerActionDescriptor
        {
            EndpointMetadata = metadata.ToList(),
        };
        var apiDescription = new ApiDescription
        {
            HttpMethod = httpMethod,
            ActionDescriptor = actionDescriptor,
        };
        var methodInfo = typeof(IdempotencyOperationFilterTests)
            .GetMethod(nameof(FixtureAction), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return new OperationFilterContext(apiDescription, null!, new SchemaRepository(), methodInfo);
    }

    private static void AssertIdempotencyDocumentation(OpenApiOperation operation)
    {
        var parameter = operation.Parameters.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("Idempotency-Key");
        parameter.In.Should().Be(ParameterLocation.Header);
        parameter.Required.Should().BeTrue();
        parameter.Schema.Type.Should().Be("string");
        parameter.Schema.Format.Should().Be("uuid");
        operation.Extensions.Should().ContainKey(IdempotencyOperationFilter.ExtensionName);
    }

    private static void FixtureAction()
    {
    }
}
