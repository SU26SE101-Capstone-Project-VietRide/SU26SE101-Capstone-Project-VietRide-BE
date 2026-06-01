using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Issues RS256 JWT access tokens.
/// Issuer: <c>vietride-identity</c>, audience: <c>vietride-api</c>, TTL: 15 minutes.
/// Claims per BSOT §6.2: iss, sub, role, operatorId, email, iat, exp, kid.
/// operatorId claim is OMITTED when the user has no operator (PASSENGER/SYSTEM_ADMIN).
/// </summary>
public sealed class RsaAccessTokenService : IAccessTokenService
{
    private const string Issuer = "vietride-identity";
    private const string Audience = "vietride-api";
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(15);

    private readonly RsaSecurityKey _signingKey;
    private readonly string _kid;
    private readonly IClock _clock;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public RsaAccessTokenService(IOptions<JwtSigningOptions> options, IClock clock)
    {
        var opts = options.Value;

        ArgumentException.ThrowIfNullOrWhiteSpace(opts.PrivateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(opts.Kid);

        var rsa = RSA.Create();
        rsa.ImportFromPem(opts.PrivateKey.AsSpan());

        _signingKey = new RsaSecurityKey(rsa) { KeyId = opts.Kid };
        _kid = opts.Kid;
        _clock = clock;
    }

    /// <inheritdoc />
    public string IssueToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = _clock.UtcNow;
        var expires = now.Add(TokenTtl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("role", user.Role.ToString()),
            new("email", user.Email),
        };

        // operatorId claim is present only for operator-scoped roles; absent = null operator
        if (user.OperatorId.HasValue)
            claims.Add(new Claim("operatorId", user.OperatorId.Value.ToString()));

        var signingCredentials = new SigningCredentials(
            _signingKey,
            SecurityAlgorithms.RsaSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = signingCredentials,
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return _tokenHandler.WriteToken(token);
    }
}
