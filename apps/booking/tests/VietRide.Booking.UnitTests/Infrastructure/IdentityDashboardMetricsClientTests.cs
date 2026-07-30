using System.Net;
using System.Text;
using FluentAssertions;
using Polly.CircuitBreaker;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Infrastructure.Http;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class IdentityDashboardMetricsClientTests
{
    [Fact]
    public async Task GetAsync_ParsesStrictRawDtoAndUsesDateQuery()
    {
        var operatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            {
              "activeUserCount": 4,
              "approvedActiveOperatorIds": ["{{operatorId}}"],
              "userRoleCounts": [{ "role": "PASSENGER", "count": 7 }],
              "operatorStatusCounts": [{ "status": "APPROVED", "count": 2 }]
            }
            """));
        var client = CreateClient(handler);

        var result = await client.GetAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        result.ActiveUserCount.Should().Be(4);
        result.ApprovedActiveOperatorIds.Should().Equal(operatorId);
        result.UserRoleCounts.Should().Equal(new IdentityDashboardUserRoleCountDto("PASSENGER", 7));
        result.OperatorStatusCounts.Should().Equal(new IdentityDashboardOperatorStatusCountDto("APPROVED", 2));
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/internal/v1/admin/dashboard/identity-metrics?from=2026-07-01&to=2026-07-31");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"activeUserCount\":-1,\"approvedActiveOperatorIds\":[],\"userRoleCounts\":[],\"operatorStatusCounts\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"activeUserCount\":1,\"approvedActiveOperatorIds\":[\"00000000-0000-0000-0000-000000000000\"],\"userRoleCounts\":[],\"operatorStatusCounts\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"activeUserCount\":1,\"approvedActiveOperatorIds\":[],\"userRoleCounts\":[{\"role\":\"PASSENGER\",\"count\":-1}],\"operatorStatusCounts\":[]}")]
    public async Task GetAsync_MapsHttpOrMalformedPayloadTo503(HttpStatusCode status, string body)
    {
        var client = CreateClient(new StubHandler(_ => Json(status, body)));

        var act = () => client.GetAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var exception = await act.Should().ThrowAsync<AdminDashboardUnavailableException>();
        exception.Which.StatusCode.Should().Be(503);
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task GetAsync_MapsTransportTimeoutTo503()
    {
        var client = CreateClient(new StubHandler(_ => throw new TaskCanceledException("timeout")));

        var act = () => client.GetAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        await act.Should().ThrowAsync<AdminDashboardUnavailableException>();
    }

    [Fact]
    public async Task GetAsync_MapsOpenCircuitTo503()
    {
        var client = CreateClient(new StubHandler(_ => throw new BrokenCircuitException("open")));

        var act = () => client.GetAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        await act.Should().ThrowAsync<AdminDashboardUnavailableException>();
    }

    [Fact]
    public async Task GetAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = CreateClient(new StubHandler(_ => throw new OperationCanceledException(cancellation.Token)));

        var act = () => client.GetAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IdentityDashboardMetricsClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity-service"),
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
