using System.Text.Json;
using FluentAssertions;
using Google.Apis.Auth;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Auth.GoogleLogin;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Security;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application;

public sealed class GoogleLoginCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow =
        new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private readonly IGoogleIdTokenVerifier _googleIdTokenVerifier = Substitute.For<IGoogleIdTokenVerifier>();
    private readonly IOAuthIdentityRepository _oauthIdentities = Substitute.For<IOAuthIdentityRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IAccessTokenService _accessTokenService = Substitute.For<IAccessTokenService>();
    private readonly ILoginLockoutCounter _loginLockoutCounter = Substitute.For<ILoginLockoutCounter>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public GoogleLoginCommandHandlerTests()
    {
        _clock.UtcNow.Returns(FrozenNow);
        _accessTokenService.IssueToken(Arg.Any<User>()).Returns("access-token");
        _oauthIdentities.AddAsync(Arg.Any<OAuthIdentity>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<OAuthIdentity>()));
        _users.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<User>()));
        _refreshTokens.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<RefreshToken>()));
        _outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_WhenOAuthIdentityExists_LogsInExistingLinkedUser()
    {
        var user = MakeActivePassenger("linked@example.com");
        user.UpdateAvatar("https://cdn.example.test/custom-avatar.png");
        var googleUser = MakeGoogleUser("google-sub-1", "linked@example.com");
        var handler = CreateHandler();

        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns(googleUser);
        _oauthIdentities.GetUserByProviderSubjectAsync(
                OAuthProvider.GOOGLE,
                googleUser.Subject,
                Arg.Any<CancellationToken>())
            .Returns(user);
        _users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        result.Should().BeEquivalentTo(new TokenBundleDto(
            AccessToken: "access-token",
            RefreshToken: result.RefreshToken,
            ExpiresInSeconds: 900,
            User: new UserSummaryDto(
                Id: user.Id,
                Email: user.Email,
                Phone: user.Phone?.Value,
                DisplayName: user.DisplayName,
                Role: user.Role.ToString(),
                OperatorId: user.OperatorId,
                Status: user.Status.ToString(),
                AvatarUrl: "https://cdn.example.test/custom-avatar.png")));
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        user.AvatarUrl.Should().Be("https://cdn.example.test/custom-avatar.png");
        _accessTokenService.Received(1).IssueToken(user);
        await _users.DidNotReceiveWithAnyArgs().GetByEmailAsync(default!, default);
        await _oauthIdentities.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailExistsWithoutOAuthIdentity_CreatesLinkAndLogsInExistingUser()
    {
        var user = MakeActivePassenger("Existing@Example.com");
        user.UpdateAvatar("https://cdn.example.test/custom-avatar.png");
        var googleUser = MakeGoogleUser("google-sub-2", "Existing@Example.com");
        var handler = CreateHandler();

        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns(googleUser);
        _oauthIdentities.GetUserByProviderSubjectAsync(
                OAuthProvider.GOOGLE,
                googleUser.Subject,
                Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _users.GetByEmailAsync("existing@example.com", Arg.Any<CancellationToken>())
            .Returns(user);
        _users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        result.User.Id.Should().Be(user.Id);
        result.User.AvatarUrl.Should().Be("https://cdn.example.test/custom-avatar.png");
        user.AvatarUrl.Should().Be("https://cdn.example.test/custom-avatar.png");
        _accessTokenService.Received(1).IssueToken(user);
        await _oauthIdentities.Received(1).AddAsync(
            Arg.Is<OAuthIdentity>(identity => identity.UserId == user.Id
                && identity.Provider == OAuthProvider.GOOGLE
                && identity.ProviderSubject == googleUser.Subject
                && identity.ProviderEmail == "existing@example.com"),
            Arg.Any<CancellationToken>());
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailIsNew_CreatesGoogleAccountLinkAndLogsInNewUser()
    {
        var googleUser = MakeGoogleUser("google-sub-3", "new@example.com");
        var handler = CreateHandler();
        string? capturedEventType = null;
        string? capturedPayload = null;

        _outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedEventType = call.ArgAt<string>(0);
                capturedPayload = call.ArgAt<string>(1);
                return Task.CompletedTask;
            });

        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns(googleUser);
        _oauthIdentities.GetUserByProviderSubjectAsync(
                OAuthProvider.GOOGLE,
                googleUser.Subject,
                Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _users.GetByEmailAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        result.User.Email.Should().Be("new@example.com");
        result.User.Status.Should().Be(UserStatus.ACTIVE.ToString());
        result.User.AvatarUrl.Should().Be(googleUser.AvatarUrl);
        await _users.Received(1).AddAsync(
            Arg.Is<User>(user => user.Email == "new@example.com"
                && user.Phone == null
                && user.Status == UserStatus.ACTIVE
                && user.AvatarUrl == googleUser.AvatarUrl),
            Arg.Any<CancellationToken>());
        await _oauthIdentities.Received(1).AddAsync(
            Arg.Is<OAuthIdentity>(identity => identity.Provider == OAuthProvider.GOOGLE
                && identity.ProviderSubject == googleUser.Subject
                && identity.ProviderEmail == "new@example.com"),
            Arg.Any<CancellationToken>());
        _accessTokenService.Received(1).IssueToken(Arg.Is<User>(user => user.Email == "new@example.com"));

        await _outbox.Received(1).EnqueueAsync(
            UserCreatedIntegrationEvent.EventType,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        capturedEventType.Should().Be(UserCreatedIntegrationEvent.EventType);
        capturedPayload.Should().NotBeNull();
        using var payload = JsonDocument.Parse(capturedPayload!);
        var root = payload.RootElement;
        root.GetProperty("userId").GetGuid().Should().Be(result.User.Id);
        root.GetProperty("role").GetString().Should().Be(UserRole.PASSENGER.ToString());
        root.GetProperty("email").GetString().Should().Be("new@example.com");
        root.GetProperty("createdAt").GetDateTimeOffset().Should().Be(FrozenNow);
        root.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["userId", "role", "email", "createdAt"]);
    }

    [Fact]
    public async Task Handle_WhenStoredAvatarIsNull_OmitsAvatarFromSerializedUser()
    {
        var googleUser = MakeGoogleUser("google-sub-no-avatar", "no-avatar@example.com", avatarUrl: null);
        var handler = CreateHandler();

        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns(googleUser);
        _oauthIdentities.GetUserByProviderSubjectAsync(
                OAuthProvider.GOOGLE,
                googleUser.Subject,
                Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _users.GetByEmailAsync("no-avatar@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        result.User.AvatarUrl.Should().BeNull();
        var json = JsonSerializer.Serialize(result.User, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("\"avatarUrl\"");
    }

    [Fact]
    public async Task Handle_WhenLinkedUserIsLocked_ThrowsForbiddenAndDoesNotIssueTokens()
    {
        var user = MakeActivePassenger("locked@example.com");
        user.Lock();
        var googleUser = MakeGoogleUser("google-sub-locked", "locked@example.com");
        var handler = CreateHandler();

        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns(googleUser);
        _oauthIdentities.GetUserByProviderSubjectAsync(
                OAuthProvider.GOOGLE,
                googleUser.Subject,
                Arg.Any<CancellationToken>())
            .Returns(user);
        _users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_ACCOUNT_LOCKED");
        _accessTokenService.DidNotReceiveWithAnyArgs().IssueToken(default!);
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_WhenGoogleTokenIsInvalid_ThrowsUnauthorizedWithGoogleCode()
    {
        var handler = CreateHandler();
        _googleIdTokenVerifier.VerifyAsync("bad-token", Arg.Any<CancellationToken>())
            .Returns<Task<GoogleIdTokenVerificationResult>>(_ => throw new InvalidJwtException("invalid token"));

        var act = () => handler.Handle(new GoogleLoginCommand("bad-token"), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<UnauthorizedException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_GOOGLE_TOKEN_INVALID");
        await _oauthIdentities.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGoogleVerifierHasOperationalConfigError_PropagatesError()
    {
        var handler = CreateHandler();
        _googleIdTokenVerifier.VerifyAsync("id-token", Arg.Any<CancellationToken>())
            .Returns<Task<GoogleIdTokenVerificationResult>>(_ => throw new InvalidOperationException("Google OAuth client id is not configured."));

        var act = () => handler.Handle(new GoogleLoginCommand("id-token"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Google OAuth client id is not configured.");
        await _oauthIdentities.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private GoogleLoginCommandHandler CreateHandler()
    {
        return new GoogleLoginCommandHandler(
            _googleIdTokenVerifier,
            _oauthIdentities,
            _users,
            _refreshTokens,
            _accessTokenService,
            new RefreshTokenFactory(_clock),
            _loginLockoutCounter,
            _outbox,
            _clock);
    }

    private static GoogleIdTokenVerificationResult MakeGoogleUser(
        string subject,
        string email,
        string? avatarUrl = "https://example.test/avatar.png")
    {
        return new GoogleIdTokenVerificationResult(
            Subject: subject,
            Email: email,
            DisplayName: "Google User",
            AvatarUrl: avatarUrl);
    }

    private static User MakeActivePassenger(string email)
    {
        var user = User.CreatePassenger(
            email,
            TestPhone,
            "$2a$12$hashedpassword",
            "Existing User");

        user.VerifyEmail();
        return user;
    }
}
