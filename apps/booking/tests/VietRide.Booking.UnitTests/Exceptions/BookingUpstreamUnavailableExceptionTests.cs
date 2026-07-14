using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Booking.Application.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;

namespace VietRide.Booking.UnitTests.Exceptions;

public sealed class BookingUpstreamUnavailableExceptionTests
{
    [Fact]
    public void Exception_HasFixedPublicMapping()
    {
        ICodedHttpException exception = new BookingUpstreamUnavailableException("Identity failed.");
        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", exception.ErrorCode);
    }

    [Fact]
    public void ApiResponseExceptionFilter_EmitsUpstreamUnavailable502Envelope()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["X-Request-Id"] = "trace-19";
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var context = new ExceptionContext(actionContext, [])
        {
            Exception = new BookingUpstreamUnavailableException("Identity failed."),
        };
        var filter = new ApiResponseExceptionFilter(
            NullLogger<ApiResponseExceptionFilter>.Instance);

        filter.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse>(result.Value);
        Assert.False(envelope.Success);
        Assert.Equal(StatusCodes.Status502BadGateway, envelope.StatusCode);
        Assert.Equal("UPSTREAM_UNAVAILABLE", envelope.Error.Code);
        Assert.NotEqual(StatusCodes.Status500InternalServerError, envelope.StatusCode);
        Assert.True(context.ExceptionHandled);
    }
}
