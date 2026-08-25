using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Infrastructure.DependencyInjection;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoongDirectionsShuttleDistanceClientTests
{
    [Fact]
    public async Task CalculateAsync_WhenGoongReturnsCompleteLeg_ShouldReturnDistance()
    {
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request, distanceFactory: _ => 1_234));

        var result = await CreateClient(handler).CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeEquivalentTo(new ShuttleDistanceOutcome.Success(1_234));
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Destinations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CalculateAsync_WhenGoongReturnsHttpError_ShouldFailClosed(HttpStatusCode statusCode)
    {
        var client = CreateClient(new GoongDirectionsTestHandler(_ =>
            GoongDirectionsTestHandler.Raw(statusCode)));

        var result = await client.CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeOfType<ShuttleDistanceOutcome.Unavailable>();
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("empty-routes")]
    [InlineData("wrong-count")]
    [InlineData("wrong-order")]
    [InlineData("negative-distance")]
    [InlineData("negative-duration")]
    public async Task CalculateAsync_WhenResponseIsInvalid_ShouldFailClosed(string invalidCase)
    {
        var handler = new GoongDirectionsTestHandler(request => invalidCase switch
        {
            "malformed" => GoongDirectionsTestHandler.Raw(HttpStatusCode.OK, "{invalid"),
            "empty-routes" => GoongDirectionsTestHandler.Raw(HttpStatusCode.OK, "{\"routes\":[]}"),
            "wrong-count" => GoongDirectionsTestHandler.Success(request, legCount: 0),
            "wrong-order" => GoongDirectionsTestHandler.Success(request, reverseFirstLeg: true),
            "negative-distance" => GoongDirectionsTestHandler.Success(request, distanceFactory: _ => -1),
            _ => GoongDirectionsTestHandler.Success(request, durationFactory: _ => -1),
        });

        var result = await CreateClient(handler).CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeOfType<ShuttleDistanceOutcome.Unavailable>();
    }

    [Fact]
    public async Task CalculateAsync_WhenGoongTimesOut_ShouldFailClosed()
    {
        var result = await CreateClient(new GoongDelayedHandler(), timeoutMs: 5)
            .CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeOfType<ShuttleDistanceOutcome.Unavailable>();
        ((ShuttleDistanceOutcome.Unavailable)result).Message.Should().Contain("timed out");
    }

    [Fact]
    public async Task CalculateAsync_WhenProviderIsLocal_ShouldNotCallHttp()
    {
        var handler = new GoongDirectionsTestHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called."));

        var result = await CreateClient(handler, provider: "LOCAL")
            .CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeOfType<ShuttleDistanceOutcome.Unavailable>();
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldUseShuttleDistanceTimeoutKeyAndDefault()
    {
        using var configuredHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS"] = "1234",
                ["TRACKING_ROUTING_TIMEOUT_MS"] = "1",
            })
            .Build();

        _ = new GoongDirectionsShuttleDistanceClient(configuredHttpClient, configured);

        configuredHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_234));

        using var defaultHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        _ = new GoongDirectionsShuttleDistanceClient(
            defaultHttpClient,
            new ConfigurationBuilder().Build());
        defaultHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_500));
    }

    [Fact]
    public async Task RegisteredHttpClient_ShouldNotLogGoongQueryOrApiKey()
    {
        const string apiKey = "shuttle-secret-key";
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request));
        var configuration = CreateConfiguration(apiKey: apiKey);
        var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(logs));
        InfrastructureServiceCollectionExtensions
            .AddGoongShuttleDistanceClient(services, configuration)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IShuttleDistanceClient>()
            .CalculateAsync(10m, 106m, 10.1m, 106.1m, default);

        result.Should().BeOfType<ShuttleDistanceOutcome.Success>();
        var captured = string.Join('\n', logs.Messages);
        captured.Contains("api_key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        captured.Contains(apiKey, StringComparison.Ordinal).Should().BeFalse();
    }

    private static GoongDirectionsShuttleDistanceClient CreateClient(
        HttpMessageHandler handler,
        string provider = "GOONG",
        int timeoutMs = 3_000)
    {
        var configuration = CreateConfiguration(provider, timeoutMs: timeoutMs);
        return new GoongDirectionsShuttleDistanceClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://rsapi.goong.io/") },
            configuration);
    }

    private static IConfiguration CreateConfiguration(
        string provider = "GOONG",
        string apiKey = "fake-key",
        int timeoutMs = 3_000) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ROUTING_PROVIDER"] = provider,
                ["GOONG_API_KEY"] = apiKey,
                ["GOONG_MAX_DESTINATIONS_PER_REQUEST"] = "10",
                ["TRIP_SHUTTLE_DISTANCE_TIMEOUT_MS"] = timeoutMs.ToString(),
            })
            .Build();
}
