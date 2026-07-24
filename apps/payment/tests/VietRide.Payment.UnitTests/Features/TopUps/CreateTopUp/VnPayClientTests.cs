using FluentAssertions;
using Microsoft.Extensions.Options;
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
            ReturnUrl = "https://app.vietride.app/payments/return",
            MinimumTopUpAmount = 10_000,
        });
        var client = new VnPayClient(options);
        var vnPayTxnRef = Guid.NewGuid().ToString("D");

        var url = client.CreateTopUpRedirectUrl(
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            vnPayTxnRef,
            "203.0.113.10",
            new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero));

        url.Should().StartWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?");
        url.Should().Contain("vnp_TxnRef=");
        url.Should().Contain(vnPayTxnRef);
        url.Should().Contain("vnp_Amount=10000000");
        url.Should().Contain("vnp_CreateDate=20260612170000");
        url.Should().Contain("vnp_ExpireDate=20260612171500");
        url.Should().NotContain("vnp_BankCode=");
        url.Should().Contain("vnp_SecureHash=");
        var secureHash = ExtractQueryValue(url, "vnp_SecureHash");
        secureHash.Should().HaveLength(128);
        secureHash.Should().MatchRegex("^[0-9a-f]{128}$");
    }

    [Fact]
    public void CreateTopUpRedirectUrl_ConfiguredBankCode_AddsItBeforeSigning()
    {
        var options = Options.Create(new VnPayOptions
        {
            TmnCode = "TESTTMN",
            HashSecret = "secret-key",
            BaseUrl = "https://sandbox.vnpayment.vn",
            ReturnUrl = "https://app.vietride.app/payments/return",
            BankCode = "NCB",
            MinimumTopUpAmount = 10_000,
        });
        var client = new VnPayClient(options);

        var url = client.CreateTopUpRedirectUrl(
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            Guid.NewGuid().ToString("D"),
            "203.0.113.10",
            new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero));

        var parameters = ParseQuery(url);
        parameters.Should().Contain("vnp_BankCode", "NCB");
        client.VerifySignature(parameters).Should().BeTrue();
    }

    private static string ExtractQueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Single(parts => string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))[1];
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => System.Net.WebUtility.UrlDecode(parts[0]),
                parts => System.Net.WebUtility.UrlDecode(parts[1]),
                StringComparer.Ordinal);
    }
}
