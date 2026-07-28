using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayClient : IVnPayClient
{
    private const string PayCommand = "pay";
    private const string Version = "2.1.0";
    private const string CurrencyCode = "VND";
    private const string Locale = "vn";
    private const string DefaultOrderType = "other";
    // Redis is only a short-lived concurrency lease. PostgreSQL payment/top-up status is the
    // durable idempotency source, so a crashed or rolled-back callback must become retryable soon.
    private static readonly TimeSpan IpnReservationTtl = TimeSpan.FromMinutes(1);

    private readonly VnPayOptions _options;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<VnPayClient> _logger;

    public VnPayClient(
        IOptions<VnPayOptions> options,
        IConnectionMultiplexer? redis = null,
        ILogger<VnPayClient>? logger = null)
    {
        _options = options.Value;
        _redis = redis;
        _logger = logger ?? NullLogger<VnPayClient>.Instance;
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

        return BuildRedirectUrl(
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt,
            $"VietRide wallet top-up {vnPayTxnRef} for user {userId}");
    }

    public string CreateBookingPaymentRedirectUrl(
        Guid bookingId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt)
        => BuildRedirectUrl(
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt,
            $"VietRide booking payment {bookingId} for user {userId}");

    public string CreateBookingPaymentRedirectUrl(
        Guid bookingId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
        => BuildRedirectUrl(
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt,
            $"VietRide booking payment {bookingId} for user {userId}",
            expiresAt);

    public string CreateSubscriptionPaymentRedirectUrl(
        Guid upgradeAttemptId,
        Guid operatorId,
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null)
        => BuildRedirectUrl(
            amount,
            vnPayTxnRef,
            clientIpAddress,
            createdAt,
            $"VietRide subscription payment {upgradeAttemptId} for operator {operatorId}",
            expiresAt);

    private string BuildRedirectUrl(
        Money amount,
        string vnPayTxnRef,
        string clientIpAddress,
        DateTimeOffset createdAt,
        string orderInfo,
        DateTimeOffset? expiresAt = null)
    {
        EnsureSignatureOptions();

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
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = DefaultOrderType,
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_TxnRef"] = vnPayTxnRef,
            ["vnp_ExpireDate"] = FormatVnPayDate(expiresAt ?? createdAt.AddMinutes(_options.PaymentTimeoutMinutes)),
        };

        var hashData = BuildQuery(parameters);
        var secureHash = Sign(hashData, _options.HashSecret);
        parameters["vnp_SecureHash"] = secureHash;

        return BuildPaymentUrl(parameters);
    }

    public bool VerifySignature(IReadOnlyDictionary<string, string> parameters)
    {
        EnsureSignatureOptions();

        if (!parameters.TryGetValue("vnp_SecureHash", out var secureHash) || string.IsNullOrWhiteSpace(secureHash))
            return false;

        var filtered = parameters
            .Where(pair => !string.Equals(pair.Key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair.Key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var query = BuildQuery(new SortedDictionary<string, string>(filtered, StringComparer.Ordinal));
        var expected = Sign(query, _options.HashSecret);
        return string.Equals(expected, secureHash, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsExpectedMerchant(IReadOnlyDictionary<string, string> parameters)
        => parameters.TryGetValue("vnp_TmnCode", out var tmnCode)
            && string.Equals(tmnCode, _options.TmnCode, StringComparison.Ordinal);

    public async Task<bool> TryReserveIpnAsync(string vnPayTxnRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vnPayTxnRef))
            throw new ArgumentException("VNPay transaction reference is required.", nameof(vnPayTxnRef));

        cancellationToken.ThrowIfCancellationRequested();

        if (_redis is null)
            return true;

        var db = _redis.GetDatabase();
        return await db.StringSetAsync(
            BuildIpnDedupeKey(vnPayTxnRef),
            "1",
            IpnReservationTtl,
            When.NotExists,
            CommandFlags.None);
    }

    public async Task ReleaseIpnReservationAsync(string vnPayTxnRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vnPayTxnRef))
            throw new ArgumentException("VNPay transaction reference is required.", nameof(vnPayTxnRef));

        cancellationToken.ThrowIfCancellationRequested();

        if (_redis is null)
            return;

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildIpnDedupeKey(vnPayTxnRef), CommandFlags.None);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release VNPay IPN reservation for transaction {VnPayTxnRef}; the short TTL will recover it.",
                vnPayTxnRef);
        }
    }

    private static string BuildIpnDedupeKey(string vnPayTxnRef)
        => $"payment:vnpay_ipn:v2:{vnPayTxnRef}";

    private string BuildPaymentUrl(SortedDictionary<string, string> parameters)
    {
        var baseUri = ResolvePaymentBaseUri(_options.BaseUrl);
        return $"{baseUri}?{BuildQuery(parameters)}";
    }

    private void EnsureSignatureOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.TmnCode))
            throw new PaymentVnPayException("VNPay sandbox is not configured.");

        if (string.IsNullOrWhiteSpace(_options.HashSecret))
            throw new PaymentVnPayException("VNPay sandbox is not configured.");
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
