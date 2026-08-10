using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Filters;

public sealed class ApiResponseExceptionFilterTests
{
    private static ApiResponseExceptionFilter CreateFilter()
        => new(NullLogger<ApiResponseExceptionFilter>.Instance);

    private static ExceptionContext BuildContext(Exception ex)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/v1/test";

        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new ActionDescriptor());

        return new ExceptionContext(actionContext, []) { Exception = ex };
    }

    // ------------------------------------------------------------------
    // Happy-path: each known exception arm maps to the correct status/code
    // ------------------------------------------------------------------

    [Fact]
    public void BadRequestException_Maps_To_400_With_ErrorCode()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new BadRequestException("AUTH_OTP_INVALID", "Invalid OTP"));

        filter.OnException(ctx);

        ctx.ExceptionHandled.Should().BeTrue();
        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(400);

        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.StatusCode.Should().Be(400);
        envelope.Error.Code.Should().Be("AUTH_OTP_INVALID");
        envelope.Error.Fields.Should().BeNull();
    }

    [Fact]
    public void ValidationException_Maps_To_422_With_Fields()
    {
        var filter = CreateFilter();
        var errors = new[] { new ValidationError("email", "Invalid format") };
        var ctx = BuildContext(new VietRide.Shared.Application.Exceptions.ValidationException("Validation failed", errors));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(422);

        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Fields.Should().HaveCount(1);
        envelope.Error.Fields![0].Field.Should().Be("email");
    }

    [Fact]
    public void CodedValidationException_Maps_To_422_With_ErrorCode_And_Fields()
    {
        var filter = CreateFilter();
        var errors = new[] { new ValidationError("orderIndex", "Order index is already used.") };
        var ctx = BuildContext(new CodedValidationException("ROUTE_STOP_ORDER_CONFLICT", "Route stop order index is already used.", errors));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(422);

        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("ROUTE_STOP_ORDER_CONFLICT");
        envelope.Error.Fields.Should().HaveCount(1);
        envelope.Error.Fields![0].Field.Should().Be("orderIndex");
    }

    [Fact]
    public void NotFoundException_Maps_To_404()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new NotFoundException("User", Guid.NewGuid()));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(404);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData("STATION_NOT_FOUND")]
    [InlineData("STOP_NOT_FOUND")]
    public void CodedNotFoundException_Maps_To_404_With_Caller_ErrorCode(string errorCode)
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new CodedNotFoundException(errorCode, "Resource was not found"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(404);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.StatusCode.Should().Be(404);
        envelope.Error.Code.Should().Be(errorCode);
        envelope.Error.Message.Should().Be("Resource was not found");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("station_not_found")]
    [InlineData("STATION-NOT-FOUND")]
    [InlineData("STATION__NOT_FOUND")]
    [InlineData("STATION_NOT_FOUND_")]
    public void CodedNotFoundException_Rejects_Invalid_ErrorCode(string errorCode)
    {
        var act = () => new CodedNotFoundException(errorCode, "Resource was not found");

        act.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void ConflictException_Maps_To_409()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email taken"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(409);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public void ForbiddenException_Maps_To_403()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new ForbiddenException("AUTH_ACCOUNT_LOCKED", "Account locked"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(403);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("AUTH_ACCOUNT_LOCKED");
    }

    [Fact]
    public void UnauthorizedException_Maps_To_401()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new UnauthorizedException("AUTH_TOKEN_INVALID", "Token invalid"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(401);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("AUTH_TOKEN_INVALID");
    }

    [Fact]
    public void TooManyRequestsException_Maps_To_429()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new TooManyRequestsException("AUTH_OTP_RATE_LIMIT_EXCEEDED", "Rate limit hit"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(429);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("AUTH_OTP_RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public void DomainException_Maps_To_422_With_ErrorCode()
    {
        // A local sealed subclass simulates any service-layer DomainException
        // without adding a cross-project reference to the Identity assembly.
        var filter = CreateFilter();
        var ctx = BuildContext(new StubDomainException("DRIVER_NOT_ELIGIBLE", "Driver does not meet criteria"));

        filter.OnException(ctx);

        ctx.ExceptionHandled.Should().BeTrue();
        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(422);

        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.StatusCode.Should().Be(422);
        envelope.Error.Code.Should().Be("DRIVER_NOT_ELIGIBLE");
        envelope.Error.Fields.Should().BeNull();
    }

    [Fact]
    public void PaymentInsufficientWalletDomainException_Maps_To_402()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new StubDomainException("PAYMENT_INSUFFICIENT_WALLET", "Wallet balance is insufficient."));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(402);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("PAYMENT_INSUFFICIENT_WALLET");
        envelope.StatusCode.Should().Be(402);
    }

    [Fact]
    public void UnknownException_Maps_To_500_INTERNAL_ERROR()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new InvalidOperationException("Unexpected"));

        filter.OnException(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(500);
        var envelope = result.Value.Should().BeOfType<ApiResponse>().Subject;
        envelope.Error.Code.Should().Be("INTERNAL_ERROR");
    }

    // ------------------------------------------------------------------
    // Envelope invariants
    // ------------------------------------------------------------------

    [Fact]
    public void Envelope_Meta_Has_TraceId_And_Timestamp()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new NotFoundException("X", 1));
        ctx.HttpContext.Items[VietRide.Shared.Web.Middleware.RequestLoggingMiddleware.RequestIdHeader] = "req-123";

        filter.OnException(ctx);

        var result = (ObjectResult)ctx.Result!;
        var envelope = (ApiResponse)result.Value!;
        envelope.Meta.TraceId.Should().Be("req-123");
        envelope.Meta.Timestamp.Should().NotBe(default);
    }

    [Fact]
    public void Envelope_Success_Is_False_For_Error()
    {
        var filter = CreateFilter();
        var ctx = BuildContext(new BadRequestException("SOME_CODE", "bad"));

        filter.OnException(ctx);

        var result = (ObjectResult)ctx.Result!;
        var envelope = (ApiResponse)result.Value!;
        envelope.Success.Should().BeFalse();
        envelope.StatusCode.Should().Be(400);
    }
}

// Local stub — avoids a cross-project reference to any service's Domain assembly.
// Models the DomainException subclass that every service Domain layer defines.
file sealed class StubDomainException : DomainException
{
    public StubDomainException(string errorCode, string message) : base(errorCode, message) { }
}
