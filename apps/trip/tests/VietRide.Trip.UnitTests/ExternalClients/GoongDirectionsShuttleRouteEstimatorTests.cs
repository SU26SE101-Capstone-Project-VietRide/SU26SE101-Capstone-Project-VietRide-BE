using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoongDirectionsShuttleRouteEstimatorTests
{
    [Fact]
    public async Task EstimateDurationAsync_ShouldAccumulateEveryOrderedLeg()
    {
        var durations = new[] { 600d, 900d, 1_200d };
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request, durationFactory: index => durations[index]));
        var estimator = CreateEstimator(handler);

        var result = await estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            [
                new ShuttleRouteCoordinate(10.71m, 106.61m),
                new ShuttleRouteCoordinate(10.72m, 106.62m),
                new ShuttleRouteCoordinate(10.73m, 106.63m),
            ]);

        result.Should().Be(TimeSpan.FromMinutes(45));
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task EstimateDurationAsync_ShouldChunkAndChainAtConfiguredLimit()
    {
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request, durationFactory: _ => 60d));
        var estimator = CreateEstimator(handler, maxDestinations: 2);
        var destinations = Enumerable.Range(1, 5)
            .Select(index => new ShuttleRouteCoordinate(10.70m + index / 100m, 106.60m + index / 100m))
            .ToArray();

        var result = await estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            destinations);

        result.Should().Be(TimeSpan.FromMinutes(5));
        handler.Requests.Select(request => request.Destinations.Count).Should().Equal(2, 2, 1);
        handler.Requests[1].Origin.Should().Be(handler.Requests[0].Destinations[^1]);
        handler.Requests[2].Origin.Should().Be(handler.Requests[1].Destinations[^1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EstimateDurationAsync_WhenGoongReturnsHttpError_ReturnsUnavailable(HttpStatusCode statusCode)
    {
        var estimator = CreateEstimator(new GoongDirectionsTestHandler(_ =>
            GoongDirectionsTestHandler.Raw(statusCode)));

        var result = await estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            [new ShuttleRouteCoordinate(10.71m, 106.61m)]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EstimateDurationAsync_WhenPayloadIsMalformed_ReturnsUnavailable()
    {
        var estimator = CreateEstimator(new GoongDirectionsTestHandler(_ =>
            GoongDirectionsTestHandler.Raw(HttpStatusCode.OK, "{invalid")));

        var result = await estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            [new ShuttleRouteCoordinate(10.71m, 106.61m)]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EstimateDurationAsync_WhenGoongTimesOut_ReturnsUnavailable()
    {
        var result = await CreateEstimator(new GoongDelayedHandler(), timeoutMs: 5)
            .EstimateDurationAsync(
                new ShuttleRouteCoordinate(10.70m, 106.60m),
                [new ShuttleRouteCoordinate(10.71m, 106.61m)]);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EstimateDurationAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var estimator = CreateEstimator(new GoongDelayedHandler());

        var action = () => estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            [new ShuttleRouteCoordinate(10.71m, 106.61m)],
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EstimateDurationAsync_WhenProviderIsNotConfigured_DoesNotCallHttp()
    {
        var handler = new GoongDirectionsTestHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called."));
        var estimator = CreateEstimator(handler, provider: "LOCAL");

        var result = await estimator.EstimateDurationAsync(
            new ShuttleRouteCoordinate(10.70m, 106.60m),
            [new ShuttleRouteCoordinate(10.71m, 106.61m)]);

        result.Should().BeNull();
        handler.RequestCount.Should().Be(0);
    }

    private static GoongDirectionsShuttleRouteEstimator CreateEstimator(
        HttpMessageHandler handler,
        string provider = "GOONG",
        int maxDestinations = 10,
        int timeoutMs = 3_000)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ROUTING_PROVIDER"] = provider,
                ["GOONG_API_KEY"] = "fake-key",
                ["GOONG_MAX_DESTINATIONS_PER_REQUEST"] = maxDestinations.ToString(),
                ["SHUTTLE_ROUTE_PREVIEW_TIMEOUT_MS"] = timeoutMs.ToString(),
            })
            .Build();
        return new GoongDirectionsShuttleRouteEstimator(
            new HttpClient(handler) { BaseAddress = new Uri("https://rsapi.goong.io/") },
            configuration);
    }
}
