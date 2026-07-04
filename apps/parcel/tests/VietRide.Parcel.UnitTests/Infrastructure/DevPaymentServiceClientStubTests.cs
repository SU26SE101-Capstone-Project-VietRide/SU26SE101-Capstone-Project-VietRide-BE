using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class DevPaymentServiceClientStubTests
{
    private readonly DevPaymentServiceClient _sut = new(NullLogger<DevPaymentServiceClient>.Instance);

    [Fact]
    public async Task ChargeParcelPaymentAsync_WALLET_Returns_Success()
    {
        var result = await _sut.ChargeParcelPaymentAsync(
            "PARCEL", Guid.NewGuid(), Guid.NewGuid(), 100_000, "WALLET", "idem-1");

        result.Kind.Should().Be(ChargeOutcomeKind.Success);
        result.Result.Should().NotBeNull();
        result.Result!.Status.Should().Be("SUCCEEDED");
    }

    [Fact]
    public async Task ChargeParcelPaymentAsync_VNPAY_Returns_Pending_With_RedirectUrl()
    {
        var referenceId = Guid.NewGuid();

        var result = await _sut.ChargeParcelPaymentAsync(
            "PARCEL", referenceId, Guid.NewGuid(), 150_000, "VNPAY", "idem-2");

        result.Kind.Should().Be(ChargeOutcomeKind.Success);
        result.Result.Should().NotBeNull();
        result.Result!.Status.Should().Be("PENDING");
        result.Result.PaymentRedirectUrl.Should().Contain(referenceId.ToString("N"));
    }

    [Fact]
    public async Task ChargeParcelPaymentAsync_UnsupportedMethod_Returns_TransportError()
    {
        var result = await _sut.ChargeParcelPaymentAsync(
            "PARCEL", Guid.NewGuid(), Guid.NewGuid(), 100_000, "UNSUPPORTED", "idem-3");

        result.Kind.Should().Be(ChargeOutcomeKind.TransportError);
    }

    [Fact]
    public async Task RefundParcelPaymentAsync_Returns_Success()
    {
        var result = await _sut.RefundParcelPaymentAsync(
            Guid.NewGuid(), 50_000, "PARCEL_REFUND", Guid.NewGuid(), "idem-refund");

        result.Kind.Should().Be(RefundOutcomeKind.Success);
        result.Result.Should().NotBeNull();
    }
}
