using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly.CircuitBreaker;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Shared.Http.Handlers;

namespace VietRide.Booking.UnitTests.Infrastructure.AdminDashboard;

public sealed class PaymentRevenueSummaryClientTests
{
    [Fact]
    public async Task GetAsync_ParsesCanonicalRawDtoAndUsesInclusiveDateQuery()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {
              "period": {
                "from": "2026-07-01",
                "to": "2026-07-31",
                "timezone": "Asia/Ho_Chi_Minh"
              },
              "totalProjectRevenueVnd": 1000,
              "netTransportRevenueVnd": 700,
              "netTicketRevenueVnd": 500,
              "netParcelRevenueVnd": 200,
              "subscriptionRevenueVnd": 300,
              "paidToOperatorsVnd": 400,
              "generatedAt": "2026-08-07T00:00:00Z"
            }
            """));
        var client = CreateClient(handler);

        var result = await client.GetAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        result.Should().Be(new PaymentRevenueSummaryDto(1_000, 700, 500, 200, 300));
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/internal/v1/revenue/admin-summary?from=2026-07-01&to=2026-07-31");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"period\":{\"from\":\"2026-07-01\",\"to\":\"2026-07-31\",\"timezone\":\"UTC\"},\"totalProjectRevenueVnd\":1000,\"netTransportRevenueVnd\":700,\"netTicketRevenueVnd\":500,\"netParcelRevenueVnd\":200,\"subscriptionRevenueVnd\":300,\"generatedAt\":\"2026-08-07T00:00:00Z\"}")]
    [InlineData(HttpStatusCode.OK, "{\"period\":{\"from\":\"2026-07-01\",\"to\":\"2026-07-31\",\"timezone\":\"Asia/Ho_Chi_Minh\"},\"totalProjectRevenueVnd\":999,\"netTransportRevenueVnd\":700,\"netTicketRevenueVnd\":500,\"netParcelRevenueVnd\":200,\"subscriptionRevenueVnd\":300,\"generatedAt\":\"2026-08-07T00:00:00Z\"}")]
    public async Task GetAsync_MapsHttpOrMalformedPayloadTo503(HttpStatusCode status, string body)
    {
        var client = CreateClient(new StubHandler(_ => Json(status, body)));

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
    public async Task PaymentReportingRetryPolicy_RetriesOneTransientFailure()
    {
        var attempts = 0;
        var policy = InfrastructureServiceCollectionExtensions.CreatePaymentReportingRetryPolicy();

        using var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task PaymentReportingCircuit_OpensAfterFiveFailuresAndSuccessfulHalfOpenProbeClosesIt()
    {
        var policy = InfrastructureServiceCollectionExtensions
            .CreatePaymentReportingCircuitBreakerPolicy(TimeSpan.FromMilliseconds(20));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await policy.ExecuteAsync(() =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        }

        var whileOpen = () => policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await whileOpen.Should().ThrowAsync<BrokenCircuitException>();

        await Task.Delay(50);
        using var halfOpenProbe = await policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var afterClose = await policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        halfOpenProbe.StatusCode.Should().Be(HttpStatusCode.OK);
        afterClose.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AddInfrastructure_RegistersPaymentReportingClientWithFiveSecondResiliencePipeline()
    {
        const string clientName = nameof(IPaymentRevenueSummaryClient);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:BaseUrl"] = "http://identity:5001",
                ["Trip:BaseUrl"] = "http://trip:5002",
                ["Parcel:BaseUrl"] = "http://parcel:5005",
                ["Payment:BaseUrl"] = "http://payment:5004",
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["InternalJwt:Secret"] = "test-internal-jwt-secret-at-least-32-characters",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, registerConsumers: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPaymentRevenueSummaryClient>()
            .Should().BeOfType<PaymentRevenueSummaryClient>();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        client.BaseAddress.Should().Be(new Uri("http://payment:5004"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        var pipelineTypes = GetPipelineTypes(handler);
        pipelineTypes.Should().Contain(typeof(CorrelationIdDelegatingHandler));
        pipelineTypes.Should().Contain(typeof(InternalJwtDelegatingHandler));
        pipelineTypes.Count(type => type.Name == "PolicyHttpMessageHandler").Should().Be(2);
    }

    private static PaymentRevenueSummaryClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://payment-service"),
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static IReadOnlyList<Type> GetPipelineTypes(HttpMessageHandler handler)
    {
        var result = new List<Type>();
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            result.Add(current.GetType());

        return result;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }
}
