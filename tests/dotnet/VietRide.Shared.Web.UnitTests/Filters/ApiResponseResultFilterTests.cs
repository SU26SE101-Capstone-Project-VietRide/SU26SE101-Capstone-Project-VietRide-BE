using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Filters;

public sealed class ApiResponseResultFilterTests
{
    private static ApiResponseResultFilter CreateFilter() => new();

    private static ResultExecutingContext BuildContext(IActionResult result, string path = "/v1/resource")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = path;
        httpContext.Items[VietRide.Shared.Web.Middleware.RequestLoggingMiddleware.RequestIdHeader] = "trace-1";

        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new ActionDescriptor());

        return new ResultExecutingContext(actionContext, [], result, new object());
    }

    // ------------------------------------------------------------------
    // Happy-path: 200 ObjectResult is wrapped
    // ------------------------------------------------------------------

    [Fact]
    public void ObjectResult_200_Is_Wrapped_In_ApiResponse()
    {
        var filter = CreateFilter();
        var dto = new { Id = 1, Name = "test" };
        var ctx = BuildContext(new ObjectResult(dto) { StatusCode = 200 });

        filter.OnResultExecuting(ctx);

        var wrapped = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        wrapped.StatusCode.Should().Be(200);
        var type = wrapped.Value!.GetType();
        type.IsGenericType.Should().BeTrue();
        type.GetGenericTypeDefinition().Should().Be(typeof(ApiResponse<>));
    }

    [Fact]
    public void ObjectResult_201_Is_Wrapped_With_Created_Factory()
    {
        var filter = CreateFilter();
        var dto = new { UserId = Guid.NewGuid() };
        var ctx = BuildContext(new ObjectResult(dto) { StatusCode = 201 });

        filter.OnResultExecuting(ctx);

        var wrapped = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        wrapped.StatusCode.Should().Be(201);
        // The wrapped value should be ApiResponse<T> with Success=true, StatusCode=201
        dynamic envelope = wrapped.Value!;
        ((bool)envelope.Success).Should().BeTrue();
        ((int)envelope.StatusCode).Should().Be(201);
    }

    // ------------------------------------------------------------------
    // Error-case: 204 stays empty, no envelope
    // ------------------------------------------------------------------

    [Fact]
    public void NoContentResult_204_Is_Not_Wrapped()
    {
        var filter = CreateFilter();
        var original = new NoContentResult();
        var ctx = BuildContext(original);

        filter.OnResultExecuting(ctx);

        // Result is unchanged — NoContentResult is not an ObjectResult with a value
        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public void ObjectResult_With_StatusCode_204_Is_Not_Wrapped()
    {
        var filter = CreateFilter();
        var original = new ObjectResult(null) { StatusCode = 204 };
        var ctx = BuildContext(original);

        filter.OnResultExecuting(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    // ------------------------------------------------------------------
    // No double-wrap
    // ------------------------------------------------------------------

    [Fact]
    public void Already_Wrapped_ApiResponse_Is_Not_Double_Wrapped()
    {
        var filter = CreateFilter();
        var alreadyWrapped = ApiResponse<string>.Ok("data", ApiMeta.Create("t"));
        var ctx = BuildContext(new ObjectResult(alreadyWrapped) { StatusCode = 200 });

        filter.OnResultExecuting(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.Value.Should().BeSameAs(alreadyWrapped);
    }

    [Fact]
    public void Already_Wrapped_ApiResponse_Error_Is_Not_Double_Wrapped()
    {
        var filter = CreateFilter();
        var error = ApiResponse.Failure(409, new ApiError { Code = "CONFLICT", Message = "m" }, ApiMeta.Create("t"));
        var ctx = BuildContext(new ObjectResult(error) { StatusCode = 409 });

        filter.OnResultExecuting(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.Value.Should().BeSameAs(error);
    }

    // ------------------------------------------------------------------
    // Exempt paths
    // ------------------------------------------------------------------

    [Fact]
    public void WellKnown_Path_Is_Skipped()
    {
        var filter = CreateFilter();
        var dto = new { Keys = new[] { "key1" } };
        var original = new ObjectResult(dto) { StatusCode = 200 };
        var ctx = BuildContext(original, "/.well-known/jwks.json");

        filter.OnResultExecuting(ctx);

        // Result unchanged — well-known is exempt from the envelope (Q-v7.5.1)
        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public void Actual_V1_Jwks_Path_Is_Skipped()
    {
        var filter = CreateFilter();
        var dto = new { Keys = new[] { "key1" } };
        var original = new ObjectResult(dto) { StatusCode = 200 };
        var ctx = BuildContext(original, "/v1/.well-known/jwks.json");

        filter.OnResultExecuting(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public void Health_Path_Is_Skipped()
    {
        var filter = CreateFilter();
        var dto = new { Status = "Healthy" };
        var original = new ObjectResult(dto) { StatusCode = 200 };
        var ctx = BuildContext(original, "/health");

        filter.OnResultExecuting(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    // ------------------------------------------------------------------
    // Meta traceId propagation
    // ------------------------------------------------------------------

    [Fact]
    public void Meta_TraceId_Is_Populated_From_Request_Items()
    {
        var filter = CreateFilter();
        var dto = new { Value = 42 };
        var ctx = BuildContext(new ObjectResult(dto) { StatusCode = 200 });
        ctx.HttpContext.Items[VietRide.Shared.Web.Middleware.RequestLoggingMiddleware.RequestIdHeader] = "req-abc";

        filter.OnResultExecuting(ctx);

        var result = (ObjectResult)ctx.Result!;
        dynamic envelope = result.Value!;
        string traceId = envelope.Meta.TraceId;
        traceId.Should().Be("req-abc");
    }
}
