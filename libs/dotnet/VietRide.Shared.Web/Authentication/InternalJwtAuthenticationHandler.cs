using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace VietRide.Shared.Web.Authentication;

/// Verifies X-Internal-Auth bearer token (Internal JWT HS256, TTL 120s) signed by Gateway.
/// Per BACKEND_SOURCE_OF_TRUTH 5.1 + 5.3 + 3.4.1.
public static class InternalJwtAuthenticationExtensions
{
    public const string Scheme = "InternalJwt";
    public const string HeaderName = "X-Internal-Auth";

    public static AuthenticationBuilder AddInternalJwt(
        this AuthenticationBuilder builder,
        string secret,
        string issuer = "vietride-gateway",
        string audience = "vietride-internal")
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException(
                "INTERNAL_JWT_SECRET must be ≥32 chars. Configure via env var.");

        return builder.AddJwtBearer(Scheme, options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5),
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidAlgorithms = new[] { "HS256" },
            };

            // Read token from X-Internal-Auth header (Bearer prefix).
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    if (ctx.Request.Headers.TryGetValue(HeaderName, out var raw))
                    {
                        var s = raw.ToString();
                        ctx.Token = s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? s.Substring(7)
                            : s;
                    }
                    return Task.CompletedTask;
                },
            };
        });
    }
}
