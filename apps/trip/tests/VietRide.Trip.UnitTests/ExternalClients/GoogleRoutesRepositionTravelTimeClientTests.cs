using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoogleRoutesRepositionTravelTimeClientTests
{
    [Fact]
    public async Task CalculateAsync_WhenDisabled_ReturnsUnavailableWithoutCallingGoogle()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var client = CreateClient(handler, enabled: false);

        var result = await client.CalculateAsync(10.0m, 106.0m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
        result.FailureMessage.Should().Contain("not configured");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CalculateAsync_WhenGoogleReturnsDuration_RoundsUpToWholeMinutes()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"routes\":[{\"duration\":\"61s\",\"distanceMeters\":1200}]}",
                Encoding.UTF8,
                "application/json"),
        });
        var client = CreateClient(handler, enabled: true);

        var result = await client.CalculateAsync(10.0m, 106.0m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeTrue();
        result.DurationMinutes.Should().Be(2);
        result.DistanceMeters.Should().Be(1200);
    }

    [Fact]
    public async Task CalculateAsync_WhenGoogleTimesOut_ReturnsUnavailable()
    {
        var handler = new DelayedHandler();
        var client = CreateClient(handler, enabled: true, timeoutMs: 5);

        var result = await client.CalculateAsync(10.0m, 106.0m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
        result.FailureMessage.Should().Contain("timed out");
    }

    private static GoogleRoutesRepositionTravelTimeClient CreateClient(
        HttpMessageHandler handler,
        bool enabled,
        int timeoutMs = 3_000)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GOOGLE_ROUTES_ENABLED"] = enabled.ToString(),
                ["GOOGLE_ROUTES_API_KEY"] = enabled ? "test-key" : string.Empty,
                ["RESOURCE_TRAVEL_TIME_TIMEOUT_MS"] = timeoutMs.ToString(),
            })
            .Build();
        return new GoogleRoutesRepositionTravelTimeClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://routes.googleapis.com/") },
            configuration);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
