using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class GoogleRoutesTripEtaPlannerTests
{
    [Fact]
    public async Task PlanAsync_ShouldAccumulateLegDurationsAndStopDwell()
    {
        var fixture = CreateFixture(2, routeDurationMinutes: 120);
        var handler = new RoutesHandler(_ => ["600s", "900s", "1200s"]);
        var planner = CreatePlanner(handler);

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.GOOGLE_ROUTES);
        result.StopArrivalTimes[fixture.Stops[0].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(10));
        result.StopArrivalTimes[fixture.Stops[1].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(45));
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(85));
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task PlanAsync_ShouldFallbackForUnexpectedLegCount()
    {
        var fixture = CreateFixture(2, routeDurationMinutes: 120);
        var planner = CreatePlanner(new RoutesHandler(_ => ["600s"]));

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.ROUTE_BASELINE);
        result.StopArrivalTimes[fixture.Stops[0].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(10));
        result.StopArrivalTimes[fixture.Stops[1].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(40));
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(160));
    }

    [Fact]
    public async Task PlanAsync_ShouldChunkRoutesWithMoreThan25IntermediateWaypoints()
    {
        var fixture = CreateFixture(27, routeDurationMinutes: 600);
        var handler = new RoutesHandler(targetCount => Enumerable.Repeat("60s", targetCount).ToArray());
        var planner = CreatePlanner(handler);

        var result = await planner.PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.GOOGLE_ROUTES);
        result.StopArrivalTimes.Should().HaveCount(27);
        result.StopArrivalTimes[fixture.Stops[^1].Stop.Id]
            .Should().Be(fixture.Departure.AddMinutes(547));
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(568));
        handler.RequestCount.Should().Be(2);
        handler.TargetCounts.Should().Equal(26, 2);
    }

    [Fact]
    public async Task PlanAsync_ShouldFallbackWithoutCallingGoogleWhenCoordinatesAreMissing()
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);
        fixture.Origin.UpdateProfile(
            fixture.Origin.Name,
            fixture.Origin.Slug,
            fixture.Origin.City,
            fixture.Origin.Ward,
            fixture.Origin.AddressStreet,
            fixture.Origin.LocationId,
            latitude: null,
            longitude: null,
            fixture.Origin.ContactPhone,
            fixture.Origin.ContactEmail,
            fixture.Origin.OperatingHours,
            fixture.Origin.Facilities,
            fixture.Origin.SupportsShuttle);
        var handler = new RoutesHandler(_ => ["60s", "60s"]);

        var result = await CreatePlanner(handler).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.ROUTE_BASELINE);
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task PlanAsync_ShouldFallbackWhenGoogleRejectsTheRequest()
    {
        var fixture = CreateFixture(1, routeDurationMinutes: 90);

        var result = await CreatePlanner(new FailureHandler(HttpStatusCode.TooManyRequests)).PlanAsync(
            fixture.Route,
            fixture.Origin,
            fixture.Destination,
            fixture.Stops,
            fixture.Departure);

        result.Source.Should().Be(PlannedEtaSource.ROUTE_BASELINE);
        result.DestinationArrivalTime.Should().Be(fixture.Departure.AddMinutes(110));
    }

    private static GoogleRoutesTripEtaPlanner CreatePlanner(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GOOGLE_ROUTES_ENABLED"] = "true",
                ["GOOGLE_ROUTES_API_KEY"] = "fake-key",
                ["TRIP_STOP_DWELL_MINUTES"] = "20",
                ["TRIP_PLANNED_ETA_TIMEOUT_MS"] = "3000",
            })
            .Build();
        return new GoogleRoutesTripEtaPlanner(
            new HttpClient(handler) { BaseAddress = new Uri("https://routes.googleapis.com/") },
            configuration);
    }

    private static Fixture CreateFixture(int stopCount, int routeDurationMinutes)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "HCM", "Ward 1", latitude: 10.7m, longitude: 106.6m);
        var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Da Nang", "Ward 2", latitude: 16.1m, longitude: 108.2m);
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
                var stop = Stop.Create(operatorId, $"Stop {index}", 10.7m + index / 100m, 106.6m + index / 100m);
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

    private sealed class RoutesHandler(Func<int, IReadOnlyList<string>> durationsFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<int> TargetCounts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var intermediateCount = document.RootElement.GetProperty("intermediates").GetArrayLength();
            var targetCount = intermediateCount + 1;
            TargetCounts.Add(targetCount);
            var legs = durationsFactory(targetCount).Select(duration => new { duration }).ToArray();
            var json = JsonSerializer.Serialize(new { routes = new[] { new { legs } } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FailureHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
