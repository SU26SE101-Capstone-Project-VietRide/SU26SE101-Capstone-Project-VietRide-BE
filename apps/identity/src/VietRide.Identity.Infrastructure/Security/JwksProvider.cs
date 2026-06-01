using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Produces the JWKS document for the Identity Service RS256 public key.
/// Exposes only the public key components: kty, alg, use, kid, n (modulus), e (exponent).
/// </summary>
public sealed class JwksProvider : IJwksProvider
{
    private readonly string _jwksJson;

    public JwksProvider(IOptions<JwtSigningOptions> options)
    {
        var opts = options.Value;

        ArgumentException.ThrowIfNullOrWhiteSpace(opts.PrivateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.Kid);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(opts.PrivateKey.AsSpan());

        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        // Base64Url-encode the modulus (n) and exponent (e)
        var n = Base64UrlEncode(parameters.Modulus!);
        var e = Base64UrlEncode(parameters.Exponent!);

        var jwks = new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    alg = "RS256",
                    use = "sig",
                    kid = opts.Kid,
                    n,
                    e,
                },
            },
        };

        _jwksJson = JsonSerializer.Serialize(jwks, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        });
    }

    /// <inheritdoc />
    public string GetJwks() => _jwksJson;

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
