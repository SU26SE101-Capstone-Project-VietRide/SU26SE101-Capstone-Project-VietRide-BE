using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Polly.CircuitBreaker;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Infrastructure.DependencyInjection;
using VietRide.Parcel.Infrastructure.Http;
using VietRide.Shared.Http.Handlers;

namespace VietRide.Parcel.UnitTests.Infrastructure.ParcelReport;

public sealed class PaymentOperatorRevenueSummaryClientTests
{
    private static readonly Guid OperatorId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task GetAsync_ParsesCanonicalParcelMoneyAndUsesInclusiveDateQuery()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            {
              "period": {
                "from": "2026-07-01",
                "to": "2026-07-31",
                "timezone": "Asia/Ho_Chi_Minh"
              },
              "operatorId": "{{OperatorId}}",
              "netRevenueVnd": 800,
              "netTicketRevenueVnd": 500,
              "netParcelRevenueVnd": 300,
              "grossParcelRevenueVnd": 350,
              "parcelRefundsVnd": -50,
              "generatedAt": "2026-08-07T00:00:00Z"
            }
            """));
        var client = CreateClient(handler);

        var result = await client.GetAsync(
            OperatorId,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        result.Should().Be(new PaymentOperatorRevenueSummaryDto(350, -50, 300));
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            $"/internal/v1/revenue/operators/{OperatorId:D}/summary?from=2026-07-01&to=2026-07-31");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"period\":{\"from\":\"2026-07-01\",\"to\":\"2026-07-31\",\"timezone\":\"Asia/Ho_Chi_Minh\"},\"operatorId\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\",\"netRevenueVnd\":800,\"netTicketRevenueVnd\":500,\"netParcelRevenueVnd\":301,\"grossParcelRevenueVnd\":350,\"parcelRefundsVnd\":-50,\"generatedAt\":\"2026-08-07T00:00:00Z\"}")]
    [InlineData(HttpStatusCode.OK, "{\"period\":{\"from\":\"2026-07-01\",\"to\":\"2026-07-31\",\"timezone\":\"UTC\"},\"operatorId\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\",\"netRevenueVnd\":800,\"netTicketRevenueVnd\":500,\"netParcelRevenueVnd\":300,\"grossParcelRevenueVnd\":350,\"parcelRefundsVnd\":-50,\"generatedAt\":\"2026-08-07T00:00:00Z\"}")]
    public async Task GetAsync_MapsHttpOrMalformedPayloadTo503(HttpStatusCode status, string body)
    {
        var client = CreateClient(new StubHandler(_ => Json(status, body)));

        var act = () => client.GetAsync(
            OperatorId,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        var exception = await act.Should().ThrowAsync<ParcelDependencyUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task GetAsync_MapsOpenCircuitTo503()
    {
        var client = CreateClient(new StubHandler(_ => throw new BrokenCircuitException("open")));

        var act = () => client.GetAsync(
            OperatorId,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        await act.Should().ThrowAsync<ParcelDependencyUnavailableException>();
    }

    [Fact]
    public async Task ReportingRetryPolicy_RetriesOneTransientFailure()
    {
        var attempts = 0;
        var policy = InfrastructureServiceCollectionExtensions.CreatePaymentReportingRetryPolicy();

        using var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(
                attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ReportingCircuit_OpensAfterFiveFailuresAndRejectsCalls()
    {
        var policy = InfrastructureServiceCollectionExtensions
            .CreatePaymentReportingCircuitBreakerPolicy();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await policy.ExecuteAsync(() =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        }

        var whileOpen = () => policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await whileOpen.Should().ThrowAsync<BrokenCircuitException>();
    }

    [Fact]
    public async Task ReportingCircuit_HalfOpenSuccessClosesIt()
    {
        var policy = InfrastructureServiceCollectionExtensions
            .CreatePaymentReportingCircuitBreakerPolicy(TimeSpan.FromMilliseconds(20));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await policy.ExecuteAsync(() =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        }

        await Task.Delay(50);
        using var halfOpenProbe = await policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var afterClose = await policy.ExecuteAsync(() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        halfOpenProbe.StatusCode.Should().Be(HttpStatusCode.OK);
        afterClose.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AddInfrastructure_RegistersReportingClientWithFiveSecondResiliencePipeline()
    {
        const string clientName = nameof(IPaymentOperatorRevenueSummaryClient);
        var configuration = Configuration(usePaymentStub: false);
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns("Testing");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, hostEnvironment, registerConsumers: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPaymentOperatorRevenueSummaryClient>()
            .Should().BeOfType<PaymentOperatorRevenueSummaryClient>();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        client.BaseAddress.Should().Be(new Uri("http://payment:5004"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        var pipelineTypes = GetPipelineTypes(handler);
        pipelineTypes.Should().Contain(typeof(CorrelationIdDelegatingHandler));
        pipelineTypes.Should().Contain(typeof(InternalJwtDelegatingHandler));
        pipelineTypes.Count(type => type.Name == "PolicyHttpMessageHandler").Should().Be(2);
    }

    [Fact]
    public async Task AddInfrastructure_WhenPaymentDevStubEnabled_ReportClientFailsClosed()
    {
        var configuration = Configuration(usePaymentStub: true);
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns("Testing");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, hostEnvironment, registerConsumers: false);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPaymentOperatorRevenueSummaryClient>();

        var act = () => client.GetAsync(
            OperatorId,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        await act.Should().ThrowAsync<ParcelDependencyUnavailableException>();
    }

    private static PaymentOperatorRevenueSummaryClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://payment-service"),
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static IConfiguration Configuration(bool usePaymentStub)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = usePaymentStub.ToString(),
                ["Booking:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:BaseUrl"] = "http://payment:5004",
                ["InternalJwt:Secret"] = "test-internal-jwt-secret-at-least-32-characters",
            })
            .Build();

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
