using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Security;

namespace VietRide.Identity.UnitTests.Security;

/// <summary>
/// The emailed set-password link has to land on a page the recipient can actually use,
/// and the two audiences need different pages:
///
///   DRIVER / ASSISTANT  → /auth/set-password         → gateway's "open in app" page
///   everyone else       → /auth/set-initial-password → operator web password form
///
/// /auth/set-password is load-bearing for the mobile flow: it is the exact path Android
/// App Links watches (assetlinks.json + the driver app's intent filter) and the path
/// nginx routes to the gateway. These tests pin both paths so a future refactor cannot
/// quietly repoint drivers at a web form or staff at an "install the app" page.
/// </summary>
public sealed class InitialPasswordTokenServiceTests
{
    private const string PublicAppUrl = "https://vietride.online";

    private static InitialPasswordTokenService CreateService(string? publicAppUrl = PublicAppUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PUBLIC_APP_URL"] = publicAppUrl,
            })
            .Build();

        return new InitialPasswordTokenService(configuration);
    }

    [Theory]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    public void BuildSetInitialPasswordUrl_MobileAppRoles_PointAtTheDeepLinkPath(UserRole role)
    {
        var url = CreateService().BuildSetInitialPasswordUrl("abc123", role);

        url.Should().Be("https://vietride.online/auth/set-password?token=abc123");
    }

    [Theory]
    [InlineData(UserRole.OPERATOR_STAFF)]
    [InlineData(UserRole.OPERATOR_ADMIN)]
    [InlineData(UserRole.SYSTEM_ADMIN)]
    [InlineData(UserRole.PASSENGER)]
    public void BuildSetInitialPasswordUrl_WebRoles_PointAtTheOperatorWebPath(UserRole role)
    {
        var url = CreateService().BuildSetInitialPasswordUrl("abc123", role);

        url.Should().Be("https://vietride.online/auth/set-initial-password?token=abc123");
    }

    [Fact]
    public void BuildSetInitialPasswordUrl_DriverAndStaff_DoNotShareALandingPage()
    {
        var service = CreateService();

        var driverUrl = service.BuildSetInitialPasswordUrl("abc123", UserRole.DRIVER);
        var staffUrl = service.BuildSetInitialPasswordUrl("abc123", UserRole.OPERATOR_STAFF);

        driverUrl.Should().NotBe(staffUrl);
    }

    [Fact]
    public void BuildSetInitialPasswordUrl_EscapesTheToken()
    {
        var url = CreateService().BuildSetInitialPasswordUrl("a b&c=d", UserRole.OPERATOR_STAFF);

        url.Should().Be("https://vietride.online/auth/set-initial-password?token=a%20b%26c%3Dd");
    }

    [Fact]
    public void BuildSetInitialPasswordUrl_TrimsATrailingSlashOnPublicAppUrl()
    {
        var url = CreateService("https://vietride.online/")
            .BuildSetInitialPasswordUrl("abc123", UserRole.DRIVER);

        url.Should().Be("https://vietride.online/auth/set-password?token=abc123");
    }
}
