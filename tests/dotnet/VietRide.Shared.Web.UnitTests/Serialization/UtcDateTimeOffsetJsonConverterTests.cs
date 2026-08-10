using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Web.Serialization;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Serialization;

public sealed class UtcDateTimeOffsetJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UtcDateTimeOffsetJsonConverter(), new UtcDateTimeJsonConverter() },
    };

    private static readonly JsonSerializerOptions FrontendOptions = new()
    {
        Converters =
        {
            new UtcDateTimeOffsetJsonConverter(() => true),
            new UtcDateTimeJsonConverter(() => true),
        },
    };

    [Fact]
    public void Serialize_AlwaysEmitsUtcWithZ()
    {
        var value = new DateTimeOffset(2026, 8, 10, 17, 0, 0, TimeSpan.FromHours(7));

        JsonSerializer.Serialize(value, Options).Should().Be("\"2026-08-10T10:00:00Z\"");
    }

    [Fact]
    public void Serialize_FrontendResponse_EmitsVietnamOffset()
    {
        var value = new DateTimeOffset(2026, 8, 10, 5, 0, 0, TimeSpan.Zero);

        JsonSerializer.Serialize(value, FrontendOptions)
            .Should().Be("\"2026-08-10T12:00:00+07:00\"");
    }

    [Fact]
    public void Deserialize_NormalizesExplicitOffsetToUtc()
    {
        var result = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-08-10T17:00:00+07:00\"", Options);

        result.Should().Be(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero));
        result.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Deserialize_RejectsTimestampWithoutOffset()
    {
        var action = () => JsonSerializer.Deserialize<DateTimeOffset>(
            "\"2026-08-10T17:00:00\"",
            Options);

        action.Should().Throw<JsonException>()
            .WithMessage("*RFC 3339*");
    }

    [Theory]
    [InlineData("2026-08-10 17:00:00+07:00")]
    [InlineData("2026-02-30T17:00:00Z")]
    [InlineData("2026-08-10T17:00:00z")]
    public void Deserialize_RejectsNonRfc3339Timestamp(string value)
    {
        var action = () => JsonSerializer.Deserialize<DateTimeOffset>($"\"{value}\"", Options);

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void Serialize_LegacyDateTimeInstant_AlwaysEmitsUtcWithZ()
    {
        var value = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Unspecified);

        JsonSerializer.Serialize(value, Options).Should().Be("\"2026-08-10T10:00:00Z\"");
    }

    [Fact]
    public void Serialize_LegacyDateTimeInstant_FrontendResponse_EmitsVietnamOffset()
    {
        var value = new DateTime(2026, 8, 10, 5, 0, 0, DateTimeKind.Utc);

        JsonSerializer.Serialize(value, FrontendOptions)
            .Should().Be("\"2026-08-10T12:00:00+07:00\"");
    }
}
