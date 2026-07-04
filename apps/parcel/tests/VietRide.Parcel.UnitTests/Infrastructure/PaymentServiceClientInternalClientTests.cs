using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class PaymentServiceClientInternalClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FakeMessageHandler _handler = null!;

    [Fact]
    public async Task ChargeParcelPaymentAsync_Sends_Correct_Request()
    {
        var paymentId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                paymentId,
                status = "SUCCEEDED",
                paymentRedirectUrl = (string?)null,
            },
            meta = new { traceId = "t1" },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await client.ChargeParcelPaymentAsync(
            "PARCEL", referenceId, userId, 100_000, "WALLET", "idem-charge-1");

        _handler.LastRequest.Should().NotBeNull();
        _handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/internal/v1/payments/charge");
        _handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        _handler.LastRequest.Headers.TryGetValues("Idempotency-Key", out var values).Should().BeTrue();
        values!.First().Should().Be("idem-charge-1");
        _handler.LastBody.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(_handler.LastBody!, JsonOptions);
        deserialized.GetProperty("referenceType").GetString().Should().Be("PARCEL");
        deserialized.GetProperty("referenceId").GetGuid().Should().Be(referenceId);
        deserialized.GetProperty("userId").GetGuid().Should().Be(userId);
        deserialized.GetProperty("amount").GetInt64().Should().Be(100_000);
        deserialized.GetProperty("method").GetString().Should().Be("WALLET");
    }

    [Fact]
    public async Task ChargeParcelPaymentAsync_Returns_Success_On_200()
    {
        var paymentId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                paymentId,
                status = "SUCCEEDED",
                paymentRedirectUrl = (string?)null,
            },
            meta = new { traceId = "t1" },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.ChargeParcelPaymentAsync(
            "PARCEL", Guid.NewGuid(), Guid.NewGuid(), 100_000, "WALLET", "idem-1");

        result.Kind.Should().Be(ChargeOutcomeKind.Success);
        result.Result.Should().NotBeNull();
        result.Result!.PaymentId.Should().Be(paymentId);
        result.Result.Status.Should().Be("SUCCEEDED");
    }

    [Fact]
    public async Task ChargeParcelPaymentAsync_Returns_InsufficientFunds_On_ErrorCode()
    {
        var body = JsonSerializer.Serialize(new
        {
            success = false,
            statusCode = 402,
            error = new
            {
                code = "INSUFFICIENT_FUNDS",
                message = "Wallet balance insufficient.",
            },
        }, JsonOptions);

        var client = BuildClient((HttpStatusCode)402, body);

        var result = await client.ChargeParcelPaymentAsync(
            "PARCEL", Guid.NewGuid(), Guid.NewGuid(), 999_999, "WALLET", "idem-2");

        result.Kind.Should().Be(ChargeOutcomeKind.InsufficientFunds);
    }

    [Fact]
    public async Task ChargeParcelPaymentAsync_Returns_TransportError_On_Unexpected_Status()
    {
        var client = BuildClient(HttpStatusCode.InternalServerError, "{}");

        var result = await client.ChargeParcelPaymentAsync(
            "PARCEL", Guid.NewGuid(), Guid.NewGuid(), 100_000, "WALLET", "idem-3");

        result.Kind.Should().Be(ChargeOutcomeKind.TransportError);
    }

    [Fact]
    public async Task RefundParcelPaymentAsync_Returns_Success_On_200()
    {
        var txId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                walletTransactionId = txId,
                balanceAfter = 500_000L,
            },
            meta = new { traceId = "t2" },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.RefundParcelPaymentAsync(
            Guid.NewGuid(), 100_000, "PARCEL_REFUND", Guid.NewGuid(), "idem-refund");

        result.Kind.Should().Be(RefundOutcomeKind.Success);
        result.Result.Should().NotBeNull();
        result.Result!.WalletTransactionId.Should().Be(txId);
        result.Result.BalanceAfter.Should().Be(500_000);
    }

    [Fact]
    public async Task RefundParcelPaymentAsync_Returns_TransportError_On_Failure()
    {
        var client = BuildClient(HttpStatusCode.InternalServerError, "{}");

        var result = await client.RefundParcelPaymentAsync(
            Guid.NewGuid(), 100_000, "PARCEL_REFUND", Guid.NewGuid(), "idem-refund-2");

        result.Kind.Should().Be(RefundOutcomeKind.TransportError);
    }

    private PaymentServiceClient BuildClient(HttpStatusCode status, string body)
    {
        _handler = new FakeMessageHandler(status, body);
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://payment-service"),
        };
        return new PaymentServiceClient(httpClient, NullLogger<PaymentServiceClient>.Instance);
    }
}
