using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Infrastructure.Http;

public sealed class RevenueAnalyticsClientsTests
{
    [Fact]
    public async Task IdentitySummary_ParsesOptionalLogoWithoutBreakingExistingNameShape()
    {
        var operatorId = Guid.NewGuid();
        var client = Create<IIdentityOperatorSummaryClient>(
            "VietRide.Payment.Infrastructure.Http.IdentityOperatorSummaryClient",
            new StubHandler((_, _) => Json(HttpStatusCode.OK, $$"""
                [{"operatorId":"{{operatorId}}","operatorName":"Operator A","logoUrl":"https://cdn.test/logo.png"}]
                """)));

        var result = await client.GetAsync([operatorId]);

        result.Should().ContainSingle().Which.Should().Be(
            new OperatorSummaryItem(operatorId, "Operator A", "https://cdn.test/logo.png"));
    }

    [Fact]
    public async Task TripAnalyticsClient_UsesRawInternalContractsAndBatchesSummariesByOneHundred()
    {
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var requestedTripIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var summaryCalls = 0;
        var client = Create<ITripRevenueAnalyticsClient>(
            "VietRide.Payment.Infrastructure.Http.TripRevenueAnalyticsClient",
            new StubHandler(async (request, cancellationToken) =>
            {
                request.Headers.GetValues("X-Internal-Auth").Single().Should().Be("Bearer internal-token");
                if (request.RequestUri!.AbsolutePath.EndsWith("vehicle-counts/batch", StringComparison.Ordinal))
                {
                    return await Json(HttpStatusCode.OK, $$"""
                        [{"operatorId":"{{operatorId}}","vehicleCount":4}]
                        """);
                }

                if (request.RequestUri.AbsolutePath.EndsWith("route-performance", StringComparison.Ordinal))
                {
                    request.RequestUri.Query.Should().Be("?month=2026-07");
                    return await Json(HttpStatusCode.OK, $$"""
                        [{"routeId":"{{routeId}}","routeName":"A route","originName":"Origin","destinationName":"Destination","tripCount":3,"completedTripCount":2}]
                        """);
                }

                request.RequestUri.AbsolutePath.Should().Be("/internal/v1/trips/summaries/batch");
                summaryCalls++;
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var ids = document.RootElement.GetProperty("tripIds").EnumerateArray().Select(item => item.GetGuid()).ToArray();
                ids.Should().HaveCountLessThanOrEqualTo(100);
                return await Json(HttpStatusCode.OK, JsonSerializer.Serialize(ids.Select(id => new
                {
                    tripId = id,
                    status = "COMPLETED",
                    departureAt = "2026-07-01T00:00:00Z",
                    arrivalEstimate = "2026-07-01T03:00:00Z",
                    route = new { routeId, name = "A route", originName = "Origin", destinationName = "Destination" },
                    vehicle = new { vehicleId = Guid.NewGuid(), licensePlate = "51B-123.45", status = "ACTIVE" },
                    driverUserId = Guid.NewGuid(),
                    assistantUserId = (Guid?)null,
                })));
            }));

        var vehicleCounts = await client.GetVehicleCountsAsync([operatorId]);
        var routePerformance = await client.GetRoutePerformanceAsync(operatorId, "2026-07");
        var summaries = await client.GetTripSummariesAsync(requestedTripIds);

        vehicleCounts.Should().ContainSingle().Which.Should().Be(new TripVehicleCountItem(operatorId, 4));
        routePerformance.Should().ContainSingle().Which.RouteId.Should().Be(routeId);
        summaries.Should().HaveCount(101);
        summaryCalls.Should().Be(2);
    }

    [Fact]
    public async Task TripAnalyticsClient_RejectsMalformedDuplicateAndUnexpectedRows()
    {
        var operatorId = Guid.NewGuid();
        var client = Create<ITripRevenueAnalyticsClient>(
            "VietRide.Payment.Infrastructure.Http.TripRevenueAnalyticsClient",
            new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("vehicle-counts/batch", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, $$"""
                    [{"operatorId":"{{operatorId}}","vehicleCount":1},{"operatorId":"{{operatorId}}","vehicleCount":2}]
                    """)
                : Json(HttpStatusCode.OK, "{}")));

        var duplicate = () => client.GetVehicleCountsAsync([operatorId]);
        var malformed = () => client.GetRoutePerformanceAsync(operatorId, "2026-07");

        await duplicate.Should().ThrowAsync<UpstreamUnavailableException>();
        await malformed.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    [Fact]
    public async Task TripAnalyticsClient_MissingRequiredTripSummaryFailsClosed()
    {
        var client = Create<ITripRevenueAnalyticsClient>(
            "VietRide.Payment.Infrastructure.Http.TripRevenueAnalyticsClient",
            new StubHandler((_, _) => Json(HttpStatusCode.OK, "[]")));

        var act = () => client.GetTripSummariesAsync([Guid.NewGuid()]);

        await act.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    [Fact]
    public async Task TripAnalyticsClient_PropagatesCallerCancellationButMapsTimeout()
    {
        var callerCancelled = new CancellationTokenSource();
        callerCancelled.Cancel();
        var cancelledClient = Create<ITripRevenueAnalyticsClient>(
            "VietRide.Payment.Infrastructure.Http.TripRevenueAnalyticsClient",
            new StubHandler((_, cancellationToken) => throw new OperationCanceledException(cancellationToken)));
        var timeoutClient = Create<ITripRevenueAnalyticsClient>(
            "VietRide.Payment.Infrastructure.Http.TripRevenueAnalyticsClient",
            new StubHandler((_, _) => throw new TaskCanceledException("timeout")));

        var cancelled = () => cancelledClient.GetVehicleCountsAsync([Guid.NewGuid()], callerCancelled.Token);
        var timeout = () => timeoutClient.GetVehicleCountsAsync([Guid.NewGuid()]);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await timeout.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    private static TClient Create<TClient>(string typeName, HttpMessageHandler handler)
    {
        var type = typeof(VietRide.Payment.Infrastructure.PaymentDbContext).Assembly
            .GetType(typeName, throwOnError: true)!;
        return (TClient)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                new HttpClient(handler) { BaseAddress = new Uri("http://trip.test/"), Timeout = TimeSpan.FromSeconds(5) },
                new FakeTokenProvider(),
            ],
            culture: null)!;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private sealed class FakeTokenProvider : IInternalJwtTokenProvider
    {
        public string IssueToken(string subject, string? audience = null) => "internal-token";
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            this.responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}
