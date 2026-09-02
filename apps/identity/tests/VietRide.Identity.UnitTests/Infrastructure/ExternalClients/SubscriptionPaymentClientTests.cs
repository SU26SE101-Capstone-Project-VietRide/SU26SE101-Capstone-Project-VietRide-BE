using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.UnitTests.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClientTests
{
    [Fact]
    public async Task CreateAsync_SendsServerControlledOperatorWebReturnMode()
    {
        var paymentId = Guid.NewGuid();
        var client = CreateClient(async (request, cancellationToken) =>
        {
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            document.RootElement.GetProperty("returnMode").GetString().Should().Be("OPERATOR_WEB");
            document.RootElement.TryGetProperty("returnUrl", out _).Should().BeFalse();

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    statusCode = 201,
                    data = new
                    {
                        paymentId,
                        status = "PENDING_REDIRECT",
                        paymentRedirectUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                        invoiceStatus = (string?)null,
                    },
                }),
            };
        });

        var result = await client.CreateAsync(CreateRequest());

        result.PaymentId.Should().Be(paymentId);
    }

    [Fact]
    public async Task CreateAsync_SerializesEveryInternalInstantAsUtcZ()
    {
        var paymentId = Guid.NewGuid();
        string? requestBody = null;
        var client = CreateClient(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    statusCode = 201,
                    data = new
                    {
                        paymentId,
                        status = "PENDING_REDIRECT",
                        paymentRedirectUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
                        invoiceStatus = (string?)null,
                    },
                }),
            };
        });
        var request = CreateRequest() with
        {
            DueAt = new DateTimeOffset(2026, 8, 10, 5, 15, 0, TimeSpan.Zero),
        };

        var result = await client.CreateAsync(request);

        result.PaymentId.Should().Be(paymentId);
        requestBody.Should().Contain("2026-08-10T05:15:00Z");
        requestBody.Should().NotContain("+00:00");
    }

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
    public async Task CreateAsync_WhenPaymentReturnsValidationFields_PreservesFieldDetails()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new
            {
                success = false,
                statusCode = 422,
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "One or more validation errors occurred.",
                    fields = new[]
                    {
                        new { field = "Amount", message = "Amount must be greater than 0." },
                    },
                },
                meta = new
                {
                    traceId = "payment-validation-test",
                    timestamp = DateTimeOffset.UtcNow,
                },
            }),
        }));

        var action = () => client.CreateAsync(CreateRequest());

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        exception.Which.Errors.Should().ContainSingle(error =>
            error.Field == "Amount"
            && error.Message == "Amount must be greater than 0.");
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

    [Fact]
    public async Task GetStatusesAsync_WhenPaymentReturnsRawList_ReturnsStatuses()
    {
        var paymentId = Guid.NewGuid();
        var upgradeAttemptId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var periodFrom = DateTimeOffset.Parse("2026-07-22T09:00:00Z");
        var dueAt = periodFrom.AddMinutes(15);
        var client = CreateClient((request, _) =>
        {
            request.RequestUri!.PathAndQuery.Should().Contain($"upgradeAttemptId={upgradeAttemptId:D}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new
                    {
                        paymentId,
                        upgradeAttemptId,
                        operatorId,
                        operatorSubscriptionId = subscriptionId,
                        planId,
                        status = "SUCCEEDED",
                        amount = 39900000L,
                        method = "VNPAY",
                        billingPeriod = "YEARLY",
                        periodFrom,
                        periodTo = periodFrom.AddYears(1),
                        succeededAt = periodFrom.AddMinutes(5),
                        dueAt,
                    },
                }),
            });
        });

        var result = await client.GetStatusesAsync([upgradeAttemptId]);

        result.Should().ContainSingle();
        result[0].PaymentId.Should().Be(paymentId);
        result[0].UpgradeAttemptId.Should().Be(upgradeAttemptId);
        result[0].Status.Should().Be("SUCCEEDED");
        result[0].DueAt.Should().Be(dueAt);
    }

    [Theory]
    [InlineData("not-json", "text/plain")]
    [InlineData("null", "application/json")]
    public async Task GetStatusesAsync_WhenPaymentReturnsInvalidSuccessBody_ReturnsBadGatewayError(
        string body,
        string contentType)
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        }));

        var action = () => client.GetStatusesAsync([Guid.NewGuid()]);

        var exception = await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        exception.Which.StatusCode.Should().Be(502);
        exception.Which.ErrorCode.Should().Be("PAYMENT_SERVICE_INVALID_RESPONSE");
    }

    [Fact]
    public async Task GetStatusesAsync_WhenPaymentReturnsError_PreservesUpstreamStatus()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Payment unavailable", Encoding.UTF8, "text/plain"),
        }));

        var action = () => client.GetStatusesAsync([Guid.NewGuid()]);

        var exception = await action.Should().ThrowAsync<SubscriptionPaymentClientException>();
        exception.Which.StatusCode.Should().Be(503);
        exception.Which.ErrorCode.Should().Be("PAYMENT_SERVICE_ERROR");
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
                    null)),
            "OPERATOR_WEB",
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
