using Microsoft.Extensions.Configuration;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

public sealed class InitialPasswordTokenService : IInitialPasswordTokenService
{
    private readonly string _publicAppUrl;

    public InitialPasswordTokenService(IConfiguration configuration)
    {
        var configuredUrl = configuration["PUBLIC_APP_URL"];
        var environment = configuration["ASPNETCORE_ENVIRONMENT"];
        if (string.IsNullOrWhiteSpace(configuredUrl)
            && string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PUBLIC_APP_URL must be configured in production.");
        }

        _publicAppUrl = (configuredUrl ?? "http://localhost:5173").TrimEnd('/');

        if (!Uri.TryCreate(_publicAppUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("PUBLIC_APP_URL must be an absolute HTTP(S) URL.");
        }
    }

    public string GenerateCode()
        => Guid.NewGuid().ToString("D");

    public DateTimeOffset GetExpiresAt(DateTimeOffset now)
        => now.AddHours(48);

    public string BuildSetInitialPasswordUrl(string code)
        => $"{_publicAppUrl}/auth/set-password?token={Uri.EscapeDataString(code)}";
}
