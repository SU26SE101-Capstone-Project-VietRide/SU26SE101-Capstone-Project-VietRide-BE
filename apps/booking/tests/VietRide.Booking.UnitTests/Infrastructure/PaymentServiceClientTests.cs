using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Infrastructure.Http;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class PaymentServiceClientTests
{
    private static readonly Guid PaymentId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ReferenceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task ChargeAsync_WithDueAt_SendsExactDeadline()
    {
        var dueAt = new DateTimeOffset(2026, 7, 31, 10, 7, 0, TimeSpan.Zero);
        var handler = new FakeMessageHandler(
            HttpStatusCode.OK,
            $$"""
            {
              "success": true,
              "statusCode": 200,
              "data": {
                "paymentId": "{{PaymentId}}",
                "status": "PENDING_REDIRECT",
                "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
              }
            }
            """);
        var client = BuildClient(handler);

        var outcome = await client.ChargeAsync(
            "BOOKING",
            ReferenceId,
            UserId,
            350_000,
            "VNPAY",
            "44444444-4444-4444-8444-444444444444",
            dueAt: dueAt);

        outcome.Should().BeOfType<ChargeOutcome.Success>();
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("dueAt").GetDateTimeOffset().Should().Be(dueAt);
    }

    [Fact]
    public async Task ChargeAsync_WhenPaymentDeadlinePassed_ReturnsTypedOutcome()
    {
        var handler = new FakeMessageHandler(
            HttpStatusCode.UnprocessableEntity,
            """
            {
              "success": false,
              "statusCode": 422,
              "error": {
                "code": "PAYMENT_DEADLINE_PASSED",
                "message": "Payment dueAt must be in the future."
              }
            }
            """);
        var client = BuildClient(handler);

        var outcome = await client.ChargeAsync(
            "BOOKING",
            ReferenceId,
            UserId,
            350_000,
            "VNPAY",
            "55555555-5555-4555-8555-555555555555",
            dueAt: DateTimeOffset.UtcNow);

        outcome.Should().BeOfType<ChargeOutcome.DeadlinePassed>()
            .Which.Message.Should().Be("Payment dueAt must be in the future.");
    }

    private static PaymentServiceClient BuildClient(FakeMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://payment-service"),
        };

        return new PaymentServiceClient(
            httpClient,
            NullLogger<PaymentServiceClient>.Instance);
    }

    private sealed class FakeMessageHandler(
        HttpStatusCode statusCode,
        string responseBody) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
