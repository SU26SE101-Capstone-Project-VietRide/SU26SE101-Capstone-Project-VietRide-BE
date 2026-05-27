using FluentAssertions;
using VietRide.Shared.Kernel.Primitives;
using Xunit;

namespace VietRide.Shared.Kernel.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_Has_Value_And_IsSuccess_True()
    {
        var r = Result<int>.Success(42);
        r.IsSuccess.Should().BeTrue();
        r.IsFailure.Should().BeFalse();
        r.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_Has_Error_And_IsFailure_True()
    {
        var err = Error.NotFound("User");
        var r = Result<int>.Failure(err);
        r.IsSuccess.Should().BeFalse();
        r.IsFailure.Should().BeTrue();
        r.Error.Should().Be(err);
    }

    [Fact]
    public void Implicit_Value_Conversion_Creates_Success()
    {
        Result<string> r = "hello";
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be("hello");
    }

    [Fact]
    public void Implicit_Error_Conversion_Creates_Failure()
    {
        Result<string> r = Error.Validation("email", "invalid format");
        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("VALIDATION_ERROR")]
    [InlineData("CONFLICT")]
    public void Error_Factories_Produce_Expected_Codes(string expectedCode)
    {
        var err = expectedCode switch
        {
            "NOT_FOUND" => Error.NotFound("X"),
            "VALIDATION_ERROR" => Error.Validation("f", "m"),
            "CONFLICT" => Error.Conflict("c"),
            _ => Error.None
        };
        err.Code.Should().Be(expectedCode);
    }
}
