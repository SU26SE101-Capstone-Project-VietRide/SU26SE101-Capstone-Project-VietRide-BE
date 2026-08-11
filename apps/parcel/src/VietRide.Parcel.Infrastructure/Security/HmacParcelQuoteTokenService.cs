using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Application.Features.Parcels.Quotes;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Infrastructure.Security;

public sealed class HmacParcelQuoteTokenService : IParcelQuoteTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;
    private readonly byte[] _secret;

    public int TtlSeconds { get; }

    public HmacParcelQuoteTokenService(ParcelQuoteTokenOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret) || options.Secret.Length < 32)
            throw new InvalidOperationException("PARCEL_QUOTE_TOKEN_SECRET must contain at least 32 characters.");

        _secret = Encoding.UTF8.GetBytes(options.Secret);
        TtlSeconds = options.TtlSeconds;
    }

    public string Issue(ParcelQuoteTokenPayload payload)
    {
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(encodedPayload));
        return $"{encodedPayload}.{Base64UrlEncode(signature)}";
    }

    public ParcelQuoteTokenReadOutcome Read(string token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new(ParcelQuoteTokenReadOutcomeKind.Invalid, null);

        var parts = token.Split('.');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return new(ParcelQuoteTokenReadOutcomeKind.Invalid, null);

        try
        {
            var suppliedSignature = Base64UrlDecode(parts[1]);
            var expectedSignature = HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(parts[0]));
            if (suppliedSignature.Length != expectedSignature.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                return new(ParcelQuoteTokenReadOutcomeKind.Invalid, null);
            }

            var payload = JsonSerializer.Deserialize<ParcelQuoteTokenPayload>(Base64UrlDecode(parts[0]), JsonOptions);
            if (payload is null || payload.Jti == Guid.Empty || payload.ExpiresAt <= payload.IssuedAt)
                return new(ParcelQuoteTokenReadOutcomeKind.Invalid, null);

            return payload.ExpiresAt <= now
                ? new(ParcelQuoteTokenReadOutcomeKind.Expired, payload)
                : new(ParcelQuoteTokenReadOutcomeKind.Success, payload);
        }
        catch (Exception)
        {
            return new(ParcelQuoteTokenReadOutcomeKind.Invalid, null);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(padded);
    }
}
