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
    public async Task ChargeAsync_WithPaymentContext_SerializesReferenceCode()
    {
        const string bookingCode = "VR-20260810-ABCD2345";
        var handler = new FakeMessageHandler(
            HttpStatusCode.OK,
            $$"""
            {
              "success": true,
              "statusCode": 200,
              "data": {
                "paymentId": "{{PaymentId}}",
                "status": "SUCCEEDED",
                "paymentRedirectUrl": null
              }
            }
            """);
        var client = BuildClient(handler);
        var context = new PaymentContextSnapshot(
            1,
            [
                new PaymentAllocationSnapshot(
                    ReferenceId,
                    "BOOKING",
                    Guid.Parse("44444444-4444-4444-8444-444444444444"),
                    Guid.Parse("55555555-5555-4555-8555-555555555555"),
                    350_000,
                    0,
                    0,
                    bookingCode),
            ]);

        var outcome = await client.ChargeAsync(
            "BOOKING",
            ReferenceId,
            UserId,
            350_000,
            "WALLET",
            "66666666-6666-4666-8666-666666666666",
            context: context);

        outcome.Should().BeOfType<ChargeOutcome.Success>();
        using var body = JsonDocument.Parse(handler.LastBody!);
        var allocation = body.RootElement
            .GetProperty("context")
            .GetProperty("allocations")[0];
        allocation.GetProperty("referenceCode").GetString().Should().Be(bookingCode);
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

    [Fact]
    public async Task RedirectLookupAsync_WhenSuccessful_UsesDedicatedRawInternalEndpoint()
    {
        var handler = new FakeMessageHandler(
            HttpStatusCode.OK,
            $$"""
            [
              {
                "paymentId": "{{PaymentId}}",
                "referenceType": "BOOKING",
                "referenceId": "{{ReferenceId}}",
                "amount": 350000,
                "dueAt": "2026-08-01T10:05:00Z",
                "paymentRedirectUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?token=signed"
              }
            ]
            """);
        var client = BuildRedirectLookupClient(handler);

        var result = await client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)]);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new PaymentRedirectLookupItem(
            PaymentId,
            "BOOKING",
            ReferenceId,
            350_000,
            new DateTimeOffset(2026, 8, 1, 10, 5, 0, TimeSpan.Zero),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?token=signed"));
        handler.CallCount.Should().Be(1);
        handler.LastRequestUri.Should().Be("http://payment-service/internal/v1/payments/redirect-sessions/lookup");
        handler.LastIdempotencyKey.Should().BeNull();
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("userId").GetGuid().Should().Be(UserId);
        body.RootElement.GetProperty("references").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task RedirectLookupAsync_WhenNonSuccess_FailsOpenWithoutReadingBody()
    {
        var handler = new FakeMessageHandler(HttpStatusCode.ServiceUnavailable, "signed-secret-response");
        var client = BuildRedirectLookupClient(handler);

        var result = await client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)]);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[{\"paymentId\":null}]")]
    [InlineData("[{\"paymentId\":\"11111111-1111-4111-8111-111111111111\",\"referenceType\":\"BOOKING\",\"referenceId\":\"22222222-2222-4222-8222-222222222222\",\"amount\":350000,\"dueAt\":\"2026-08-01T10:05:00Z\",\"paymentRedirectUrl\":\"not-a-url\"}]")]
    public async Task RedirectLookupAsync_WhenPayloadMalformed_FailsOpen(string responseBody)
    {
        var handler = new FakeMessageHandler(HttpStatusCode.OK, responseBody);
        var client = BuildRedirectLookupClient(handler);

        var result = await client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectLookupAsync_WhenTransportFails_FailsOpen()
    {
        var client = BuildRedirectLookupClient(new ThrowingMessageHandler(new HttpRequestException("offline")));

        var result = await client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectLookupAsync_WhenRequestTimesOut_FailsOpen()
    {
        var client = BuildRedirectLookupClient(new ThrowingMessageHandler(new TaskCanceledException("timeout")));

        var result = await client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectLookupAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var client = BuildRedirectLookupClient(
            new ThrowingMessageHandler(new OperationCanceledException(source.Token)));

        var action = () => client.LookupAsync(
            UserId,
            [new PaymentRedirectLookupReference("BOOKING", ReferenceId)],
            source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RedirectLookupAsync_WhenReferencesEmpty_DoesNotCallPayment()
    {
        var handler = new FakeMessageHandler(HttpStatusCode.OK, "[]");
        var client = BuildRedirectLookupClient(handler);

        var result = await client.LookupAsync(UserId, []);

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
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

    private static PaymentRedirectLookupClient BuildRedirectLookupClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://payment-service"),
        };

        return new PaymentRedirectLookupClient(httpClient);
    }

    private sealed class FakeMessageHandler(
        HttpStatusCode statusCode,
        string responseBody) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }
        public string? LastRequestUri { get; private set; }
        public string? LastIdempotencyKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri?.AbsoluteUri;
            LastIdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : null;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
