using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Api.Filters;

namespace VietRide.Trip.UnitTests.Api;

public sealed class RequireIdempotencyKeyAttributeTests
{
    private readonly RequireIdempotencyKeyAttribute _attribute = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OnActionExecuting_MissingOrBlankHeader_ThrowsExactCodedValidation(string? value)
    {
        var context = CreateContext(value);

        var exception = Assert.Throws<CodedValidationException>(
            () => _attribute.OnActionExecuting(context));

        exception.ErrorCode.Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        exception.Errors.Should().ContainSingle()
            .Which.Field.Should().Be(RequireIdempotencyKeyAttribute.HeaderName);
    }

    [Fact]
    public void OnActionExecuting_PresentHeader_DoesNotThrow()
    {
        var context = CreateContext(Guid.NewGuid().ToString("D"));

        var action = () => _attribute.OnActionExecuting(context);

        action.Should().NotThrow();
    }

    private static ActionExecutingContext CreateContext(string? value)
    {
        var httpContext = new DefaultHttpContext();
        if (value is not null)
        {
            httpContext.Request.Headers[RequireIdempotencyKeyAttribute.HeaderName] = value;
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?>(),
            controller: new object());
    }
}
