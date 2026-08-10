using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Kernel.Serialization;
using Xunit;

namespace VietRide.Shared.Kernel.UnitTests;

public sealed class UtcJsonTests
{
    [Fact]
    public void Serialize_NormalizesTypedInstantToLiteralZ()
    {
        var value = new
        {
            occurredAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(7)),
        };

        UtcJson.Serialize(value).Should().Contain("\"occurredAt\":\"2026-08-10T05:00:00Z\"");
    }

    [Fact]
    public void NormalizeInstants_ConvertsTemporalPropertiesButPreservesUserText()
    {
        const string json = """
            {
              "occurredAt": "2026-08-10T12:00:00+07:00",
              "nested": { "expiresAt": "2026-08-10T13:00:00+07:00" },
              "message": "2026-08-10T12:00:00+07:00"
            }
            """;

        using var document = JsonDocument.Parse(UtcJson.NormalizeInstants(json));

        document.RootElement.GetProperty("occurredAt").GetString().Should().EndWith("05:00:00.0000000Z");
        document.RootElement.GetProperty("nested").GetProperty("expiresAt").GetString()
            .Should().EndWith("06:00:00.0000000Z");
        document.RootElement.GetProperty("message").GetString()
            .Should().Be("2026-08-10T12:00:00+07:00");
    }

    [Theory]
    [InlineData("2026-08-10T12:00:00")]
    [InlineData("2026-08-10 12:00:00+07:00")]
    [InlineData("2026-02-30T12:00:00Z")]
    [InlineData("2026-08-10T12:00:00z")]
    public void TryParseInstant_RejectsNonRfc3339Values(string raw)
    {
        UtcJson.TryParseInstant(raw, out _).Should().BeFalse();
    }
}
