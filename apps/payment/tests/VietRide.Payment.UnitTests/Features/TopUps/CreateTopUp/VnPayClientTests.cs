using FluentAssertions;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.VnPay;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.TopUps.CreateTopUp;

public sealed class VnPayClientTests
{
    [Fact]
    public void CreateTopUpRedirectUrl_ReturnsSignedHmacSha512UrlWithUuidTxnRef()
    {
        var options = Options.Create(new VnPayOptions
        {
            TmnCode = "TESTTMN",
            HashSecret = "secret-key",
            BaseUrl = "https://sandbox.vnpayment.vn",
            WebReturnUrl = "https://app.vietride.online/payments/return",
            MobileSdkReturnUrl = "https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return",
            SdkScheme = "vietride",
            IsSandbox = true,
            WebEnabled = true,
            MobileSdkEnabled = true,
            MinimumTopUpAmount = 10_000,
        });
        var client = new VnPayClient(options);
        var vnPayTxnRef = Guid.NewGuid().ToString("D");

        var url = client.CreateTopUpRedirectUrl(
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            vnPayTxnRef,
            "203.0.113.10",
            new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero),
            VnPayReturnMode.MOBILE_SDK);

        url.Should().StartWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?");
        url.Should().Contain("vnp_TxnRef=");
        url.Should().Contain(vnPayTxnRef);
        url.Should().Contain("vnp_Amount=10000000");
        url.Should().Contain("vnp_CreateDate=20260612170000");
        url.Should().Contain("vnp_ExpireDate=20260612171500");
        url.Should().Contain("vnp_SecureHash=");
        var secureHash = ExtractQueryValue(url, "vnp_SecureHash");
        secureHash.Should().HaveLength(128);
        secureHash.Should().MatchRegex("^[0-9a-f]{128}$");
        Uri.UnescapeDataString(ExtractQueryValue(url, "vnp_ReturnUrl"))
            .Should().Be("https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return");

        client.GetMobileSdkConfiguration().Should().BeEquivalentTo(new
        {
            TmnCode = "TESTTMN",
            Scheme = "vietride",
            IsSandbox = true,
        });
    }

    [Fact]
    public void CreateSubscriptionPaymentRedirectUrl_UsesConfiguredWebReturnUrl()
    {
        var client = new VnPayClient(Options.Create(CreateOptions()));

        var url = client.CreateSubscriptionPaymentRedirectUrl(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(500_000),
            Guid.NewGuid().ToString("D"),
            "203.0.113.10",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(15),
            VnPayReturnMode.OPERATOR_WEB);

        Uri.UnescapeDataString(ExtractQueryValue(url, "vnp_ReturnUrl"))
            .Should().Be("https://app.vietride.online/payments/return");
    }

    [Fact]
    public void CreateBookingPaymentRedirectUrl_UsesExactSeatLockExpiryAndMobileReturnUrl()
    {
        var client = new VnPayClient(Options.Create(CreateOptions()));
        var createdAt = new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);
        var expiresAt = createdAt.AddMinutes(7);

        var url = client.CreateBookingPaymentRedirectUrl(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(150_000),
            Guid.NewGuid().ToString("D"),
            "203.0.113.10",
            createdAt,
            expiresAt,
            VnPayReturnMode.MOBILE_SDK);

        Uri.UnescapeDataString(ExtractQueryValue(url, "vnp_ReturnUrl"))
            .Should().Be("https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return");
        ExtractQueryValue(url, "vnp_ExpireDate").Should().Be("20260612170700");
    }

    [Fact]
    public void CreateTopUpRedirectUrl_WhenMobileSdkDisabled_FailsClosed()
    {
        var options = CreateOptions();
        options.MobileSdkEnabled = false;
        var client = new VnPayClient(Options.Create(options));

        var act = () => client.CreateTopUpRedirectUrl(
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            Guid.NewGuid().ToString("D"),
            "203.0.113.10",
            DateTimeOffset.UtcNow,
            VnPayReturnMode.MOBILE_SDK);

        act.Should().Throw<VnPayReturnModeDisabledException>()
            .Which.ErrorCode.Should().Be("VNPAY_MOBILE_SDK_DISABLED");
    }

    private static VnPayOptions CreateOptions() => new()
    {
        TmnCode = "TESTTMN",
        HashSecret = "secret-key",
        BaseUrl = "https://sandbox.vnpayment.vn",
        WebReturnUrl = "https://app.vietride.online/payments/return",
        MobileSdkReturnUrl = "https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return",
        SdkScheme = "vietride",
        IsSandbox = true,
        WebEnabled = true,
        MobileSdkEnabled = true,
        MinimumTopUpAmount = 10_000,
    };

    private static string ExtractQueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Single(parts => string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))[1];
    }
}
