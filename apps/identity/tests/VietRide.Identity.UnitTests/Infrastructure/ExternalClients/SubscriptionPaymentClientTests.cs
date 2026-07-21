using System.Net;
using System.Text;
using FluentAssertions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Infrastructure.ExternalClients;

namespace VietRide.Identity.UnitTests.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClientTests
{
    [Fact]
    public async Task CreateAsync_WhenPaymentTransportFails_ReturnsServiceUnavailableError()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused."));

        var action = () => client.CreateAsync(CreateRequest());

        var exception = await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        exception.Which.StatusCode.Should().Be(503);
        exception.Which.ErrorCode.Should().Be("PAYMENT_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task CreateAsync_WhenPaymentReturnsNonJsonError_PreservesUpstreamStatus()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Bad gateway", Encoding.UTF8, "text/plain"),
        }));

        var action = () => client.CreateAsync(CreateRequest());

        var exception = await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        exception.Which.StatusCode.Should().Be(502);
        exception.Which.ErrorCode.Should().Be("PAYMENT_SERVICE_ERROR");
    }

    [Fact]
    public async Task CreateAsync_WhenPaymentReturnsInvalidSuccessBody_ReturnsBadGatewayError()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain"),
        }));

        var action = () => client.CreateAsync(CreateRequest());

        var exception = await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        exception.Which.StatusCode.Should().Be(502);
        exception.Which.ErrorCode.Should().Be("PAYMENT_SERVICE_INVALID_RESPONSE");
    }

    private static SubscriptionPaymentClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(sendAsync))
        {
            BaseAddress = new Uri("http://payment:5004"),
        };
        return new SubscriptionPaymentClient(httpClient);
    }

    private static SubscriptionPaymentCreationRequest CreateRequest()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MONTHLY",
            "VNPAY",
            500_000,
            new SubscriptionPaymentSnapshot(
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Business",
                "MONTHLY",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMonths(1),
                new SubscriptionBuyerSnapshot(
                    "Operator",
                    "BRN-001",
                    "TAX-001",
                    "operator@example.com",
                    "0900000000",
                    null,
                    null,
                    null,
                    null)),
            "subscription-upgrade-test",
            "127.0.0.1");

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}
