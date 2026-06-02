using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Kernel.Primitives;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Primitives;

public sealed class ApiResponseTests
{
    [Fact]
    public void Success_Ok_Returns_Envelope_With_Success_True_And_200()
    {
        var meta = ApiMeta.Create("t1");
        var envelope = ApiResponse<string>.Ok("hello", meta);

        envelope.Success.Should().BeTrue();
        envelope.StatusCode.Should().Be(200);
        envelope.Data.Should().Be("hello");
        envelope.Meta.Should().BeSameAs(meta);
        envelope.Message.Should().BeNull();
    }

    [Fact]
    public void Success_Created_Returns_Envelope_With_201()
    {
        var meta = ApiMeta.Create("t2");
        var envelope = ApiResponse<int>.Created(42, meta, "Created!");

        envelope.Success.Should().BeTrue();
        envelope.StatusCode.Should().Be(201);
        envelope.Data.Should().Be(42);
        envelope.Message.Should().Be("Created!");
    }

    [Fact]
    public void Failure_Returns_Envelope_With_Success_False()
    {
        var meta = ApiMeta.Create("t3");
        var error = new ApiError { Code = "NOT_FOUND", Message = "not found" };
        var envelope = ApiResponse.Failure(404, error, meta);

        envelope.Success.Should().BeFalse();
        envelope.StatusCode.Should().Be(404);
        envelope.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void Success_Json_Omits_Message_When_Null()
    {
        var meta = ApiMeta.Create("t-json-success");
        var envelope = ApiResponse<string>.Ok("hello", meta);

        var json = JsonSerializer.Serialize(envelope);

        json.Should().NotContain("\"Message\"");
        json.Should().NotContain("\"message\"");
    }

    [Fact]
    public void Error_Json_Omits_Fields_When_Null()
    {
        var meta = ApiMeta.Create("t-json-error");
        var envelope = ApiResponse.Failure(404, new ApiError { Code = "NOT_FOUND", Message = "not found" }, meta);

        var json = JsonSerializer.Serialize(envelope);

        json.Should().NotContain("\"Fields\"");
        json.Should().NotContain("\"fields\"");
    }

    [Fact]
    public void ApiMeta_Create_Sets_Timestamp_As_UtcIso()
    {
        var meta = ApiMeta.Create("trace-xyz");

        meta.TraceId.Should().Be("trace-xyz");
        meta.Timestamp.Should().NotBeNullOrWhiteSpace();
        DateTimeOffset.TryParse(meta.Timestamp, out _).Should().BeTrue();
    }

    [Fact]
    public void ApiError_With_Fields_Carries_Field_Errors()
    {
        var fields = new[] { new ApiFieldError("email", "Invalid format") };
        var error = new ApiError { Code = "VALIDATION_ERROR", Message = "Failed", Fields = fields };

        error.Fields.Should().HaveCount(1);
        error.Fields![0].Field.Should().Be("email");
        error.Fields[0].Message.Should().Be("Invalid format");
    }
}
