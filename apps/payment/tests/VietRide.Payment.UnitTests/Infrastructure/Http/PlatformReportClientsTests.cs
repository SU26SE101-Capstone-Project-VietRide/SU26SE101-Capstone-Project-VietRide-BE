using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Infrastructure.Http;

public sealed class PlatformReportClientsTests
{
    [Fact]
    public async Task BookingClient_SendsInternalJwtUtcRangeAndParsesRawPayload()
    {
        var operatorId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.PathAndQuery.Should().Contain(
                "/internal/v1/reports/platform/bookings?from=2026-07-01T00%3A00%3A00.0000000Z");
            request.Headers.GetValues("X-Internal-Auth").Single()
                .Should().Be("Bearer internal-token");
            return Json(HttpStatusCode.OK, $$"""
                {"items":[{"operatorId":"{{operatorId}}","completedBookingCount":2,"bookingRevenueVnd":500000}]}
                """);
        });
        var client = Create<IBookingPlatformReportClient>(
            "VietRide.Payment.Infrastructure.Http.BookingPlatformReportClient",
            handler);

        var result = await client.GetAsync(
            DateTimeOffset.Parse("2026-07-01T07:00:00+07:00"),
            DateTimeOffset.Parse("2026-08-01T07:00:00+07:00"));

        result.Should().ContainSingle().Which.Should()
            .Be(new BookingPlatformReportItem(operatorId, 2, 500_000));
    }

    [Fact]
    public async Task ParcelClient_PropagatesCanonicalUpstreamOverflow()
    {
        var client = Create<IParcelPlatformReportClient>(
            "VietRide.Payment.Infrastructure.Http.ParcelPlatformReportClient",
            new StubHttpMessageHandler((_, _) => Json(
                HttpStatusCode.InternalServerError,
                """
                {"success":false,"error":{"code":"REPORT_VALUE_OVERFLOW"}}
                """)));

        var act = () => client.GetAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

        var exception = await act.Should().ThrowAsync<PlatformReportValueOverflowException>();
        exception.Which.ErrorCode.Should().Be("REPORT_VALUE_OVERFLOW");
    }

    [Fact]
    public async Task TripClient_RejectsDuplicateOrMalformedRowsAsUnavailable()
    {
        var operatorId = Guid.NewGuid();
        var client = Create<ITripPlatformReportClient>(
            "VietRide.Payment.Infrastructure.Http.TripPlatformReportClient",
            new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, $$"""
                {"items":[
                  {"operatorId":"{{operatorId}}","completedTripCount":1},
                  {"operatorId":"{{operatorId}}","completedTripCount":2}
                ]}
                """)));

        var act = () => client.GetAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    [Fact]
    public async Task IdentityClient_SendsRawBatchContractAndRejectsMoreThanFiveHundred()
    {
        var operatorIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be(
                "/internal/v1/operators/summaries/batch");
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            document.RootElement.GetProperty("operatorIds").GetArrayLength().Should().Be(2);
            return await Json(HttpStatusCode.OK, $$"""
                [
                  {"operatorId":"{{operatorIds[0]}}","operatorName":"Operator A"},
                  {"operatorId":"{{operatorIds[1]}}","operatorName":"Operator B"}
                ]
                """);
        });
        var client = Create<IIdentityOperatorSummaryClient>(
            "VietRide.Payment.Infrastructure.Http.IdentityOperatorSummaryClient",
            handler);

        var result = await client.GetAsync(operatorIds);
        var tooMany = () => client.GetAsync(
            Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray());

        result.Should().HaveCount(2);
        await tooMany.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BookingClient_MapsTimeoutAndOtherFiveHundredToUnavailable()
    {
        var timeoutClient = Create<IBookingPlatformReportClient>(
            "VietRide.Payment.Infrastructure.Http.BookingPlatformReportClient",
            new StubHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout")));
        var serverErrorClient = Create<IBookingPlatformReportClient>(
            "VietRide.Payment.Infrastructure.Http.BookingPlatformReportClient",
            new StubHttpMessageHandler((_, _) => Json(
                HttpStatusCode.InternalServerError,
                "{\"error\":{\"code\":\"INTERNAL_ERROR\"}}")));

        var timeout = () => timeoutClient.GetAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);
        var serverError = () => serverErrorClient.GetAsync(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

        await timeout.Should().ThrowAsync<UpstreamUnavailableException>();
        await serverError.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    private static TClient Create<TClient>(string typeName, HttpMessageHandler handler)
    {
        var type = typeof(VietRide.Payment.Infrastructure.PaymentDbContext).Assembly
            .GetType(typeName, throwOnError: true)!;
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://upstream.test/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        return (TClient)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [httpClient, new FakeTokenProvider()],
            culture: null)!;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private sealed class FakeTokenProvider : IInternalJwtTokenProvider
    {
        public string IssueToken(string subject, string? audience = null)
            => "internal-token";
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
