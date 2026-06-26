using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class InternalJwtTokenFactory : IInternalJwtTokenProvider
{
    private const string DefaultIssuer = "vietride-gateway";
    private const string DefaultAudience = "vietride-internal";
    private const string DefaultCallerService = "parcel";
    private const int DefaultTokenLifetimeSeconds = 120;

    private readonly byte[] _secretBytes;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _callerService;
    private readonly int _tokenLifetimeSeconds;

    public InternalJwtTokenFactory(IConfiguration configuration)
    {
        var secret = configuration["InternalJwt:Secret"]
            ?? Environment.GetEnvironmentVariable("INTERNAL_JWT_SECRET")
            ?? throw new InvalidOperationException(
                "INTERNAL_JWT_SECRET must be configured for Parcel internal calls.");

        if (secret.Length < 32)
        {
            throw new InvalidOperationException(
                "INTERNAL_JWT_SECRET must be at least 32 characters long.");
        }

        _secretBytes = Encoding.UTF8.GetBytes(secret);
        _issuer = configuration["InternalJwt:Issuer"] ?? DefaultIssuer;
        _audience = configuration["InternalJwt:Audience"] ?? DefaultAudience;
        _callerService = configuration["InternalJwt:CallerService"] ?? DefaultCallerService;
        _tokenLifetimeSeconds = configuration.GetValue(
            "InternalJwt:TokenLifetimeSeconds", DefaultTokenLifetimeSeconds);
    }

    public string IssueToken(string subject, string? audience = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = _issuer,
            ["aud"] = audience ?? _audience,
            ["sub"] = subject,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(_tokenLifetimeSeconds).ToUnixTimeSeconds(),
            ["role"] = "SERVICE",
            ["callerService"] = _callerService,
        };

        var headerJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        });

        var payloadJson = JsonSerializer.Serialize(
            payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{header}.{body}";
        var signature = Base64UrlEncode(HmacSha256(signingInput));

        return $"{signingInput}.{signature}";
    }

    private byte[] HmacSha256(string value)
    {
        using var hmac = new HMACSHA256(_secretBytes);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
