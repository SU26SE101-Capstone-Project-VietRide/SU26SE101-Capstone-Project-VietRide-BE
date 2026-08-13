using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Filters;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Filters;

public sealed class AllowedQueryParametersAttributeTests
{
    [Fact]
    public void OnActionExecuting_RejectsUnknownKeysWithFieldErrors()
    {
        var context = BuildContext("?page=1&isOneTime=true&unexpected=value");
        var filter = new AllowedQueryParametersAttribute("page", "pageSize");

        var exception = Assert.Throws<CodedValidationException>(() => filter.OnActionExecuting(context));

        exception.ErrorCode.Should().Be("VALIDATION_ERROR");
        exception.Errors.Select(error => error.Field).Should().Equal("isOneTime", "unexpected");
    }

    [Fact]
    public void OnActionExecuting_MatchesAllowedKeysCaseInsensitively()
    {
        var context = BuildContext("?Page=1&PAGESIZE=20");
        var filter = new AllowedQueryParametersAttribute("page", "pageSize");

        var action = () => filter.OnActionExecuting(context);

        action.Should().NotThrow();
    }

    private static ActionExecutingContext BuildContext(string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(queryString);
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
    }
}
