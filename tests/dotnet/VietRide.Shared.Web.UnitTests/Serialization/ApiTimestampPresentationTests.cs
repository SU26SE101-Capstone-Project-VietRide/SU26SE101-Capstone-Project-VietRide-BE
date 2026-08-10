using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using VietRide.Shared.Web.Serialization;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Serialization;

public sealed class ApiTimestampPresentationTests
{
    [Fact]
    public void CreateMeta_PublicApi_UsesVietnamOffset()
    {
        var context = ContextFor("/v1/trips/search");

        var result = ApiTimestampPresentation.CreateMeta(context, "trace-1");

        result.Timestamp.Offset.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public void CreateMeta_InternalApi_UsesUtcZ()
    {
        var context = ContextFor("/internal/v1/trips");

        var result = ApiTimestampPresentation.CreateMeta(context, "trace-2");

        result.Timestamp.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TransformCachedJsonForResponse_PublicApi_ConvertsNestedInstantsOnly()
    {
        var context = ContextFor("/v1/trips/search");
        var body = Encoding.UTF8.GetBytes(
            """
            {"departureDateTime":"2026-08-10T05:00:00Z","nested":[{"updatedAt":"2026-08-10T05:30:00+00:00"}],"message":"2026-08-10T05:00:00Z","description":"2026-99-99T99:99:99Z","date":"2026-08-10","time":"12:00:00"}
            """);

        var result = ApiTimestampPresentation.TransformCachedJsonForResponse(
            body,
            "application/json; charset=utf-8",
            context);
        var json = JsonNode.Parse(result)!.AsObject();

        json["departureDateTime"]!.GetValue<string>().Should().EndWith("+07:00");
        json["nested"]![0]!["updatedAt"]!.GetValue<string>().Should().EndWith("+07:00");
        json["message"]!.GetValue<string>().Should().Be("2026-08-10T05:00:00Z");
        json["description"]!.GetValue<string>().Should().Be("2026-99-99T99:99:99Z");
        json["date"]!.GetValue<string>().Should().Be("2026-08-10");
        json["time"]!.GetValue<string>().Should().Be("12:00:00");
    }

    [Fact]
    public void FrontendJsonElementConverter_ConvertsNestedDlqTimestamps()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"occurredAt\":\"2026-08-10T05:00:00Z\",\"message\":\"2026-08-10T05:00:00Z\"}");
        var options = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new FrontendJsonElementConverter(() => true) },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(document.RootElement, options);
        var result = JsonNode.Parse(json)!.AsObject();

        result["occurredAt"]!.GetValue<string>().Should().EndWith("+07:00");
        result["message"]!.GetValue<string>().Should().Be("2026-08-10T05:00:00Z");
    }

    [Fact]
    public void TransformCachedJsonForResponse_InternalApi_PreservesUtcPayload()
    {
        var context = ContextFor("/internal/v1/trips");
        var body = Encoding.UTF8.GetBytes("{\"occurredAt\":\"2026-08-10T05:00:00Z\"}");

        var result = ApiTimestampPresentation.TransformCachedJsonForResponse(
            body,
            "application/json",
            context);

        result.Should().Equal(body);
    }

    private static DefaultHttpContext ContextFor(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }
}
