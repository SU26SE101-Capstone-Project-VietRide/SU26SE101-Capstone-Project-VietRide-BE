using Microsoft.Extensions.Configuration;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Enums;

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

    // DRIVER and ASSISTANT onboard through the mobile app; every other role onboards
    // through the operator web. The two need DIFFERENT landing pages, so the path is
    // chosen per role rather than shared.
    //
    // /auth/set-password is reserved for the mobile flow and must not change: it is the
    // exact path Android App Links watches (assetlinks.json + the app's intent filter),
    // and nginx maps it to the gateway's "open in app" page. Repointing it would leave
    // drivers on a web page instead of opening the app.
    //
    // /auth/set-initial-password falls through nginx to the operator web SPA, which
    // serves the actual password form.
    private const string MobileAppPath = "/auth/set-password";
    private const string OperatorWebPath = "/auth/set-initial-password";

    public string BuildSetInitialPasswordUrl(string code, UserRole role)
    {
        var path = role is UserRole.DRIVER or UserRole.ASSISTANT
            ? MobileAppPath
            : OperatorWebPath;

        return $"{_publicAppUrl}{path}?token={Uri.EscapeDataString(code)}";
    }
}
