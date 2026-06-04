using FluentAssertions;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using VietRide.Identity.Infrastructure.Security;

namespace VietRide.Identity.UnitTests.Infrastructure;

public sealed class GoogleIdTokenVerifierTests
{
    [Fact]
    public async Task VerifyAsync_WhenTokenIsValid_ReturnsGoogleProfile()
    {
        var verifier = new GoogleIdTokenVerifier(
            Options.Create(new GoogleOAuthOptions { ClientId = "vietride-client-id" }),
            (token, settings) =>
            {
                token.Should().Be("valid-token");
                settings.Audience.Should().ContainSingle().Which.Should().Be("vietride-client-id");

                return Task.FromResult(new GoogleJsonWebSignature.Payload
                {
                    Subject = "google-sub-123",
                    Email = "passenger@example.com",
                    Name = "Passenger One",
                    Picture = "https://example.com/avatar.png"
                });
            });

        var result = await verifier.VerifyAsync("valid-token", CancellationToken.None);

        result.Subject.Should().Be("google-sub-123");
        result.Email.Should().Be("passenger@example.com");
        result.DisplayName.Should().Be("Passenger One");
        result.AvatarUrl.Should().Be("https://example.com/avatar.png");
    }

    [Fact]
    public async Task VerifyAsync_WhenGoogleRejectsToken_ThrowsTypedFailure()
    {
        var verifier = new GoogleIdTokenVerifier(
            Options.Create(new GoogleOAuthOptions { ClientId = "vietride-client-id" }),
            (_, _) => throw new InvalidJwtException("invalid token"));

        var act = () => verifier.VerifyAsync("expired-or-wrong-audience-token", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidJwtException>()
            .WithMessage("invalid token");
    }
}
