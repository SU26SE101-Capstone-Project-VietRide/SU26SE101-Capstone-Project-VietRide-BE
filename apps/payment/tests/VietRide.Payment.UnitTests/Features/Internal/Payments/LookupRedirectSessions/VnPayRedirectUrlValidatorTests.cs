using FluentAssertions;
using Microsoft.Extensions.Options;
using VietRide.Payment.Infrastructure.VnPay;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.LookupRedirectSessions;

public sealed class VnPayRedirectUrlValidatorTests
{
    private readonly VnPayRedirectUrlValidator _validator = new(Options.Create(new VnPayOptions
    {
        BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
    }));

    [Fact]
    public void IsTrusted_WhenUrlHasExactHttpsAuthority_AcceptsUrl()
    {
        var result = _validator.IsTrusted(
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=signed");

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("http://sandbox.vnpayment.vn/paymentv2/vpcpay.html")]
    [InlineData("https://user:password@sandbox.vnpayment.vn/paymentv2/vpcpay.html")]
    [InlineData("https://sandbox.vnpayment.vn.evil.example/paymentv2/vpcpay.html")]
    [InlineData("https://sandbox.vnpayment.vn:444/paymentv2/vpcpay.html")]
    public void IsTrusted_WhenUrlViolatesAuthorityRules_RejectsUrl(string url)
    {
        var result = _validator.IsTrusted(url);

        result.Should().BeFalse();
    }
}
