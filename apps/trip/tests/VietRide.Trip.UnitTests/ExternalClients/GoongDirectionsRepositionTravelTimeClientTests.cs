using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Infrastructure.DependencyInjection;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoongDirectionsRepositionTravelTimeClientTests
{
    [Fact]
    public async Task CalculateAsync_WhenGoongReturnsCompleteLeg_ShouldRoundDurationUp()
    {
        var handler = new GoongDirectionsTestHandler(request => GoongDirectionsTestHandler.Success(
            request,
            distanceFactory: _ => 1_200,
            durationFactory: _ => 61));

        var result = await CreateClient(handler).CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeTrue();
        result.DurationMinutes.Should().Be(2);
        result.DistanceMeters.Should().Be(1_200);
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

        var result = await client.CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong-count")]
    [InlineData("wrong-order")]
    [InlineData("negative-distance")]
    [InlineData("negative-duration")]
    public async Task CalculateAsync_WhenResponseIsInvalid_ShouldFailClosed(string invalidCase)
    {
        var handler = new GoongDirectionsTestHandler(request => invalidCase switch
        {
            "malformed" => GoongDirectionsTestHandler.Raw(HttpStatusCode.OK, "{invalid"),
            "wrong-count" => GoongDirectionsTestHandler.Success(request, legCount: 0),
            "wrong-order" => GoongDirectionsTestHandler.Success(request, reverseFirstLeg: true),
            "negative-distance" => GoongDirectionsTestHandler.Success(request, distanceFactory: _ => -1),
            _ => GoongDirectionsTestHandler.Success(request, durationFactory: _ => -1),
        });

        var result = await CreateClient(handler).CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_WhenGoongTimesOut_ShouldFailClosed()
    {
        var result = await CreateClient(new GoongDelayedHandler(), timeoutMs: 5)
            .CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
        result.FailureMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task CalculateAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => CreateClient(new GoongDelayedHandler())
            .CalculateAsync(10m, 106m, 10.1m, 106.1m, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CalculateAsync_WhenProviderIsLocal_ShouldNotCallHttp()
    {
        var handler = new GoongDirectionsTestHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called."));

        var result = await CreateClient(handler, provider: "LOCAL")
            .CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeFalse();
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldUseResourceTravelTimeTimeoutKeyAndDefault()
    {
        using var configuredHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOURCE_TRAVEL_TIME_TIMEOUT_MS"] = "1234",
                ["TRACKING_ROUTING_TIMEOUT_MS"] = "1",
            })
            .Build();

        _ = new GoongDirectionsRepositionTravelTimeClient(configuredHttpClient, configured);

        configuredHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_234));

        using var defaultHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        _ = new GoongDirectionsRepositionTravelTimeClient(
            defaultHttpClient,
            new ConfigurationBuilder().Build());
        defaultHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(3_000));
    }

    [Fact]
    public async Task RegisteredHttpClient_ShouldNotLogGoongQueryOrApiKey()
    {
        const string apiKey = "reposition-secret-key";
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
            .AddGoongRepositionTravelTimeClient(services, configuration)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IRepositionTravelTimeClient>()
            .CalculateAsync(10m, 106m, 10.1m, 106.1m);

        result.IsAvailable.Should().BeTrue();
        var captured = string.Join('\n', logs.Messages);
        captured.Contains("api_key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        captured.Contains(apiKey, StringComparison.Ordinal).Should().BeFalse();
    }

    private static GoongDirectionsRepositionTravelTimeClient CreateClient(
        HttpMessageHandler handler,
        string provider = "GOONG",
        int timeoutMs = 3_000)
    {
        var configuration = CreateConfiguration(provider, timeoutMs: timeoutMs);
        return new GoongDirectionsRepositionTravelTimeClient(
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
                ["RESOURCE_TRAVEL_TIME_TIMEOUT_MS"] = timeoutMs.ToString(),
            })
            .Build();
}
