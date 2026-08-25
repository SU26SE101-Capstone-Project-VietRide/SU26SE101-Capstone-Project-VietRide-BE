using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.DependencyInjection;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoongDirectionsTripEtaPlannerTests
{
    [Fact]
    public async Task PlanAsync_ShouldAccumulateOrderedLegDurationsAndStopDwell()
    {
        var fixture = CreateFixture(2, routeDurationMinutes: 120);
        var durations = new[] { 600d, 900d, 1_200d };
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request, durationFactory: index => durations[index]));
        var planner = CreatePlanner(handler);

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.GOONG);
        result.StopArrivalTimes[fixture.Stops[0].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(10));
        result.StopArrivalTimes[fixture.Stops[1].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(45));
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(85));
        handler.RequestCount.Should().Be(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Path.Should().Be("/Direction");
        handler.Requests[0].Vehicle.Should().Be("car");
        handler.Requests[0].Alternatives.Should().Be("false");
        handler.Requests[0].ApiKey.Should().Be("fake-key");
    }

    [Fact]
    public async Task PlanAsync_ShouldChunkAtConfiguredLimitAndChainPreviousDestination()
    {
        var fixture = CreateFixture(27, routeDurationMinutes: 600);
        var handler = new GoongDirectionsTestHandler(request =>
            GoongDirectionsTestHandler.Success(request));
        var planner = CreatePlanner(handler, maxDestinations: 10);

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.GOONG);
        result.StopArrivalTimes.Should().HaveCount(27);
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(568));
        handler.Requests.Select(request => request.Destinations.Count).Should().Equal(10, 10, 8);
        handler.Requests[1].Origin.Should().Be(handler.Requests[0].Destinations[^1]);
        handler.Requests[2].Origin.Should().Be(handler.Requests[1].Destinations[^1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PlanAsync_WhenGoongReturnsHttpError_ShouldFallback(HttpStatusCode statusCode)
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        var planner = CreatePlanner(new GoongDirectionsTestHandler(_ =>
            GoongDirectionsTestHandler.Raw(statusCode)));

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        AssertFallback(result, fixture);
    }

    [Fact]
    public async Task PlanAsync_WhenPayloadIsMalformed_ShouldFallback()
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        var planner = CreatePlanner(new GoongDirectionsTestHandler(_ =>
            GoongDirectionsTestHandler.Raw(HttpStatusCode.OK, "{invalid")));

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        AssertFallback(result, fixture);
    }

    [Theory]
    [InlineData("wrong-count")]
    [InlineData("wrong-order")]
    [InlineData("negative-distance")]
    [InlineData("negative-duration")]
    public async Task PlanAsync_WhenLegChainIsInvalid_ShouldFallback(string invalidCase)
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        var handler = new GoongDirectionsTestHandler(request => invalidCase switch
        {
            "wrong-count" => GoongDirectionsTestHandler.Success(request, legCount: 1),
            "wrong-order" => GoongDirectionsTestHandler.Success(request, reverseFirstLeg: true),
            "negative-distance" => GoongDirectionsTestHandler.Success(request, distanceFactory: _ => -1),
            _ => GoongDirectionsTestHandler.Success(request, durationFactory: _ => -1),
        });

        var result = await CreatePlanner(handler).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        AssertFallback(result, fixture);
    }

    [Fact]
    public async Task PlanAsync_WhenGoongTimesOut_ShouldFallback()
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);

        var result = await CreatePlanner(new GoongDelayedHandler(), timeoutMs: 5).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        AssertFallback(result, fixture);
    }

    [Fact]
    public async Task PlanAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => CreatePlanner(new GoongDelayedHandler()).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure,
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("LOCAL", "fake-key")]
    [InlineData(null, "fake-key")]
    [InlineData("GOONG", "")]
    public async Task PlanAsync_WhenGoongIsNotConfigured_ShouldFallbackWithoutHttp(
        string? provider,
        string apiKey)
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        var handler = new GoongDirectionsTestHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called."));

        var result = await CreatePlanner(handler, provider: provider, apiKey: apiKey).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        AssertFallback(result, fixture);
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldUseTripPlannedEtaTimeoutKeyAndDefault()
    {
        using var configuredHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRIP_PLANNED_ETA_TIMEOUT_MS"] = "1234",
                ["TRACKING_ROUTING_TIMEOUT_MS"] = "1",
            })
            .Build();

        _ = new GoongDirectionsTripEtaPlanner(configuredHttpClient, configured);

        configuredHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(1_234));

        using var defaultHttpClient = new HttpClient(new GoongDelayedHandler())
        {
            BaseAddress = new Uri("https://rsapi.goong.io/"),
        };
        _ = new GoongDirectionsTripEtaPlanner(
            defaultHttpClient,
            new ConfigurationBuilder().Build());
        defaultHttpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(3_000));
    }

    [Fact]
    public async Task RegisteredHttpClient_ShouldNotLogGoongQueryOrApiKey()
    {
        const string apiKey = "trip-secret-key";
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
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
            .AddGoongTripEtaPlannerClient(services, configuration)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ITripEtaPlanner>().PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.GOONG);
        var captured = string.Join('\n', logs.Messages);
        captured.Contains("api_key", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        captured.Contains(apiKey, StringComparison.Ordinal).Should().BeFalse();
    }

    private static void AssertFallback(TripEtaPlan result, Fixture fixture)
    {
        result.Source.Should().Be(PlannedEtaSource.ROUTE_BASELINE);
        result.StopArrivalTimes[fixture.Stops[0].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(10));
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(110));
    }

    private static GoongDirectionsTripEtaPlanner CreatePlanner(
        HttpMessageHandler handler,
        string? provider = "GOONG",
        string apiKey = "fake-key",
        int maxDestinations = 10,
        int timeoutMs = 3_000)
    {
        var configuration = CreateConfiguration(
            provider,
            apiKey,
            maxDestinations,
            timeoutMs);
        return new GoongDirectionsTripEtaPlanner(
            new HttpClient(handler) { BaseAddress = new Uri("https://rsapi.goong.io/") },
            configuration);
    }

    private static IConfiguration CreateConfiguration(
        string? provider = "GOONG",
        string apiKey = "fake-key",
        int maxDestinations = 10,
        int timeoutMs = 3_000) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ROUTING_PROVIDER"] = provider,
                ["GOONG_API_KEY"] = apiKey,
                ["GOONG_MAX_DESTINATIONS_PER_REQUEST"] = maxDestinations.ToString(),
                ["TRIP_STOP_DWELL_MINUTES"] = "20",
                ["TRIP_PLANNED_ETA_TIMEOUT_MS"] = timeoutMs.ToString(),
            })
            .Build();

    private static Fixture CreateFixture(int stopCount, int routeDurationMinutes)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Origin",
            $"origin-{Guid.NewGuid():N}",
            "HCM",
            "Ward 1",
            latitude: 10.7m,
            longitude: 106.6m);
        var destination = Station.Create(
            "Destination",
            $"destination-{Guid.NewGuid():N}",
            "Da Nang",
            "Ward 2",
            latitude: 16.1m,
            longitude: 108.2m);
        var route = Route.Create(
            operatorId,
            "Intercity",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            500m,
            routeDurationMinutes);
        var stops = Enumerable.Range(1, stopCount)
            .Select(index =>
            {
                var stop = Stop.Create(
                    operatorId,
                    $"Stop {index}",
                    10.7m + index / 100m,
                    106.6m + index / 100m);
                var routeStop = RouteStop.Create(route.Id, stop.Id, index, index * 10, index * 5m);
                return new TripEtaStopInput(routeStop, stop);
            })
            .ToArray();
        return new Fixture(route, origin, destination, stops, DateTimeOffset.UtcNow.AddDays(2));
    }

    private sealed record Fixture(
        Route Route,
        Station Origin,
        Station Destination,
        IReadOnlyList<TripEtaStopInput> Stops,
        DateTimeOffset Departure);
}
