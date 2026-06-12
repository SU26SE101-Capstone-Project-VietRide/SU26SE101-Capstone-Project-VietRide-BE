using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayClient : IVnPayClient
{
    private const string PayCommand = "pay";
    private const string Version = "2.1.0";
    private const string CurrencyCode = "VND";
    private const string Locale = "vn";
    private const string DefaultOrderType = "other";
    private static readonly TimeSpan TopUpWindow = TimeSpan.FromMinutes(15);

    private readonly VnPayOptions _options;

    public VnPayClient(IOptions<VnPayOptions> options)
    {
        _options = options.Value;
    }

    public string CreateTopUpRedirectUrl(
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt)
    {
        if (amount.Amount < _options.MinimumTopUpAmount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Top-up amount is below the configured minimum.");

        if (string.IsNullOrWhiteSpace(_options.TmnCode))
            throw new InvalidOperationException("VNPay TMN code is not configured.");

        if (string.IsNullOrWhiteSpace(_options.HashSecret))
            throw new InvalidOperationException("VNPay hash secret is not configured.");

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = Version,
            ["vnp_Command"] = PayCommand,
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_Amount"] = checked(amount.Amount * 100).ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = FormatVnPayDate(createdAt),
            ["vnp_CurrCode"] = CurrencyCode,
            ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(clientIpAddress) ? "127.0.0.1" : clientIpAddress,
            ["vnp_Locale"] = Locale,
            ["vnp_OrderInfo"] = $"VietRide wallet top-up {vnPayTxnRef} for user {userId}",
            ["vnp_OrderType"] = DefaultOrderType,
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_TxnRef"] = vnPayTxnRef,
            ["vnp_ExpireDate"] = FormatVnPayDate(createdAt.Add(TopUpWindow)),
        };

        var hashData = BuildQuery(parameters);
        var secureHash = Sign(hashData, _options.HashSecret);
        parameters["vnp_SecureHash"] = secureHash;

        return BuildPaymentUrl(parameters);
    }

    private string BuildPaymentUrl(SortedDictionary<string, string> parameters)
    {
        var baseUri = ResolvePaymentBaseUri(_options.BaseUrl);
        return $"{baseUri}?{BuildQuery(parameters)}";
    }

    private static Uri ResolvePaymentBaseUri(string configuredBaseUrl)
    {
        var baseUri = new Uri(configuredBaseUrl, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(baseUri.AbsolutePath) && baseUri.AbsolutePath != "/")
            return baseUri;

        return new Uri(baseUri, "/paymentv2/vpcpay.html");
    }

    private static string BuildQuery(SortedDictionary<string, string> parameters)
        => string.Join("&", parameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));

    private static string Sign(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA512(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(dataBytes)).ToLowerInvariant();
    }

    private static string FormatVnPayDate(DateTimeOffset value)
        => value.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
}
