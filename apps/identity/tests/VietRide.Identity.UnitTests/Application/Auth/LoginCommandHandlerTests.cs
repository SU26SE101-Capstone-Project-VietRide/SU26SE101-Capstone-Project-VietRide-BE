using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private static (LoginCommandHandler handler, IUserRepository users, IRefreshTokenRepository tokens) CreateHandler()
    {
        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IRefreshTokenRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var accessTokenSvc = Substitute.For<IAccessTokenService>();
        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        accessTokenSvc.IssueToken(Arg.Any<User>()).Returns("jwt.access.token");

        var (rawToken, refreshEntity) = (
            "rawtoken123",
            RefreshToken.Create(Guid.NewGuid(), "hash", Guid.NewGuid(), null, FrozenNow, FrozenNow.AddDays(30)));
        refreshFactory.Create(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns((rawToken, refreshEntity));
        tokens.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<RefreshToken>());

        var handler = new LoginCommandHandler(users, tokens, hasher, accessTokenSvc, refreshFactory, clock);
        return (handler, users, tokens);
    }

    private static User MakeActiveUser(string email = "user@example.com", string passwordHash = "stored_hash")
    {
        var user = User.CreatePassenger(email, TestPhone, passwordHash, "Test User");
        user.VerifyEmail();
        return user;
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsTokenBundle()
    {
        var (handler, users, _) = CreateHandler();
        var user = MakeActiveUser();

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>())
            .Returns(user);

        // Override hasher to return valid for this test.
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("correct_password", "stored_hash").Returns(true);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FrozenNow);

        var tokens = Substitute.For<IRefreshTokenRepository>();
        var accessTokenSvc = Substitute.For<IAccessTokenService>();
        var refreshFactory = Substitute.For<IRefreshTokenFactory>();

        accessTokenSvc.IssueToken(Arg.Any<User>()).Returns("jwt.access.token");
        var refreshEntity = RefreshToken.Create(Guid.NewGuid(), "hash", Guid.NewGuid(), null, FrozenNow, FrozenNow.AddDays(30));
        refreshFactory.Create(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(("rawtoken123", refreshEntity));
        tokens.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<RefreshToken>());

        var fullHandler = new LoginCommandHandler(users, tokens, hasher, accessTokenSvc, refreshFactory, clock);
        var result = await fullHandler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("jwt.access.token");
        result.RefreshToken.Should().Be("rawtoken123");
        result.ExpiresInSeconds.Should().Be(900);
        result.User.Email.Should().Be("user@example.com");
        result.User.Role.Should().Be(UserRole.PASSENGER.ToString());
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WrongPassword_Throws401()
    {
        var (handler, users, _) = CreateHandler();
        var user = MakeActiveUser();
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        // hasher.Verify returns false by default in CreateHandler.

        var act = () => handler.Handle(new LoginCommand("user@example.com", "wrong_password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Handle_UnverifiedEmail_Throws403()
    {
        var (handler, users, _) = CreateHandler();
        // User in PENDING_EMAIL_VERIFICATION status (not verified yet).
        var user = User.CreatePassenger("user@example.com", TestPhone, "hash", "User");

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "any_pass"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "AUTH_EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task Handle_LockedAccount_Throws403()
    {
        var (handler, users, _) = CreateHandler();
        var user = MakeActiveUser();
        user.Lock();

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "any_pass"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "AUTH_ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task Handle_UserNotFound_Throws401()
    {
        var (handler, users, _) = CreateHandler();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => handler.Handle(new LoginCommand("nobody@example.com", "pass"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");
    }
}
