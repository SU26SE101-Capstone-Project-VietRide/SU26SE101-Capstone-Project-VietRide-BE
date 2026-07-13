using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.DependencyInjection;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.DependencyInjection;

/// <summary>
/// Verifies that the <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>
/// wired by <c>AddVietRideSharedWeb</c> emits the <see cref="ApiResponse"/> error
/// envelope (BSOT §5.5 / ADR 0004 Rule 5) for model-binding 400s.
/// </summary>
public sealed class InvalidModelStateResponseFactoryTests
{
    /// <summary>
    /// Resolves <see cref="ApiBehaviorOptions"/> from a real <c>AddVietRideSharedWeb</c>
    /// registration so the test exercises the actual factory lambda, not a re-implementation.
    /// A minimal <see cref="IConfiguration"/> stub supplies only the required
    /// <c>INTERNAL_JWT_SECRET</c> so the call succeeds without booting a full host.
    /// </summary>
    private static ApiBehaviorOptions BuildOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INTERNAL_JWT_SECRET"] = "unit-test-secret-minimum-32-chars-ok",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideSharedWeb(configuration, "TestService");

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;
    }

    private static ActionContext BuildInvalidModelStateContext(
        string field = "email",
        string errorMessage = "The email field is required.")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/v1/auth/register";

        var modelState = new ModelStateDictionary();
        modelState.AddModelError(field, errorMessage);

        return new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
            modelState);
    }

    // ------------------------------------------------------------------
    // Happy-path: valid model-state with errors returns error envelope
    // ------------------------------------------------------------------

    [Fact]
    public void Factory_Returns_422_ObjectResult_With_ValidationError_Code()
    {
        var options = BuildOptions();
        var ctx = BuildInvalidModelStateContext("email", "The email field is required.");

        var result = options.InvalidModelStateResponseFactory!(ctx);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(422);

        var envelope = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.StatusCode.Should().Be(422);
        envelope.Error.Code.Should().Be("VALIDATION_ERROR");
    }

    // ------------------------------------------------------------------
    // Error-case: fields array is populated for each model-state error
    // ------------------------------------------------------------------

    [Fact]
    public void Factory_Populates_Fields_Array_With_Model_State_Errors()
    {
        var options = BuildOptions();
        var ctx = BuildInvalidModelStateContext("phone", "Invalid phone format.");

        var result = options.InvalidModelStateResponseFactory!(ctx);

        var objectResult = (ObjectResult)result;
        var envelope = (ApiResponse)objectResult.Value!;
        envelope.Error.Fields.Should().NotBeNull();
        envelope.Error.Fields!.Should().ContainSingle(f => f.Field == "phone");
    }
}
