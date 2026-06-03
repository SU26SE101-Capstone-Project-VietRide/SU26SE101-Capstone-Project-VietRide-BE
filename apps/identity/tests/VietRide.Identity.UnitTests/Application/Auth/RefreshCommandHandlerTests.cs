using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Refresh;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class RefreshCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private static RefreshCommandHandler CreateHandler(
        IUserRepository? users = null,
        IRefreshTokenRepository? refreshTokens = null,
        IAccessTokenService? accessTokenSvc = null,
        IRefreshTokenFactory? refreshFactory = null,
        IRefreshTokenFamilyRevoker? refreshTokenFamilyRevoker = null,
        IClock? clock = null)
    {
        users ??= Substitute.For<IUserRepository>();
        refreshTokens ??= Substitute.For<IRefreshTokenRepository>();
        accessTokenSvc ??= Substitute.For<IAccessTokenService>();
        refreshFactory ??= Substitute.For<IRefreshTokenFactory>();
        refreshTokenFamilyRevoker ??= Substitute.For<IRefreshTokenFamilyRevoker>();
        clock ??= Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        accessTokenSvc.IssueToken(Arg.Any<User>()).Returns("new.access.token");
        refreshFactory.ComputeHash(Arg.Any<string>()).Returns(ci => "hash_of_" + ci.Arg<string>());

        return new RefreshCommandHandler(
            users,
            refreshTokens,
            accessTokenSvc,
            refreshFactory,
            refreshTokenFamilyRevoker,
            clock);
    }

    private static User MakeActiveUser()
    {
        var u = User.CreatePassenger("u@e.com", TestPhone, "hash", "User");
        u.VerifyEmail();
        return u;
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidToken_RotatesAndReturnsNewBundle()
    {
        var user = MakeActiveUser();
        var familyId = Guid.NewGuid();
        var existingToken = RefreshToken.Create(
            userId: user.Id,
            tokenHash: "hash_of_rawtoken123",
            familyId: familyId,
            parentTokenId: null,
            issuedAt: FrozenNow.AddDays(-1),
            expiresAt: FrozenNow.AddDays(29));

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync("hash_of_rawtoken123", Arg.Any<CancellationToken>())
            .Returns(existingToken);
        refreshTokens.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<RefreshToken>());

        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        refreshFactory.ComputeHash("rawtoken123").Returns("hash_of_rawtoken123");
        var newEntity = RefreshToken.Create(user.Id, "new_hash", familyId, existingToken.Id, FrozenNow, FrozenNow.AddDays(30));
        refreshFactory.Create(user.Id, existingToken.Id, familyId).Returns(("newraw", newEntity));

        var handler = CreateHandler(users: users, refreshTokens: refreshTokens, refreshFactory: refreshFactory);

        var result = await handler.Handle(new RefreshCommand("rawtoken123"), CancellationToken.None);

        result.Should().NotBeNull();
        result.RefreshToken.Should().Be("newraw");
        result.AccessToken.Should().Be("new.access.token");

        // Old token must be revoked.
        existingToken.RevokedReason.Should().Be(RefreshTokenRevokeReason.NORMAL_ROTATION);
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_TokenNotFound_Throws401()
    {
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var handler = CreateHandler(refreshTokens: refreshTokens);

        var act = () => handler.Handle(new RefreshCommand("nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task Handle_ExpiredToken_Throws401WithAuthTokenInvalid()
    {
        var user = MakeActiveUser();
        var familyId = Guid.NewGuid();

        // Token issued yesterday, expired one second before FrozenNow.
        var expiredToken = RefreshToken.Create(
            userId: user.Id,
            tokenHash: "hash_of_expiredraw",
            familyId: familyId,
            parentTokenId: null,
            issuedAt: FrozenNow.AddDays(-30),
            expiresAt: FrozenNow.AddSeconds(-1));

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync("hash_of_expiredraw", Arg.Any<CancellationToken>())
            .Returns(expiredToken);

        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        refreshFactory.ComputeHash("expiredraw").Returns("hash_of_expiredraw");

        var handler = CreateHandler(refreshTokens: refreshTokens, refreshFactory: refreshFactory);

        var act = () => handler.Handle(new RefreshCommand("expiredraw"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_TOKEN_INVALID");

        // Family must NOT be revoked — expiry is not a reuse-detection event.
        await refreshTokens.DidNotReceive().RevokeFamilyAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LockedUser_Throws401WithoutRotatingToken()
    {
        var user = MakeActiveUser();
        user.Lock();
        var familyId = Guid.NewGuid();
        var existingToken = RefreshToken.Create(
            userId: user.Id,
            tokenHash: "hash_of_lockedraw",
            familyId: familyId,
            parentTokenId: null,
            issuedAt: FrozenNow.AddDays(-1),
            expiresAt: FrozenNow.AddDays(29));

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync("hash_of_lockedraw", Arg.Any<CancellationToken>())
            .Returns(existingToken);

        var accessTokenSvc = Substitute.For<IAccessTokenService>();
        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        refreshFactory.ComputeHash("lockedraw").Returns("hash_of_lockedraw");

        var handler = CreateHandler(
            users: users,
            refreshTokens: refreshTokens,
            accessTokenSvc: accessTokenSvc,
            refreshFactory: refreshFactory);

        var act = () => handler.Handle(new RefreshCommand("lockedraw"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_TOKEN_INVALID");

        existingToken.RevokedAt.Should().BeNull();
        await refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        accessTokenSvc.DidNotReceive().IssueToken(Arg.Any<User>());
        refreshFactory.DidNotReceive().Create(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NormalRotationRevokedTokenWithinGrace_Throws401WithoutFamilyRevoke()
    {
        var user = MakeActiveUser();
        var familyId = Guid.NewGuid();
        var revokedToken = RefreshToken.Create(
            userId: user.Id,
            tokenHash: "hash_of_parallel",
            familyId: familyId,
            parentTokenId: null,
            issuedAt: FrozenNow.AddMinutes(-1),
            expiresAt: FrozenNow.AddDays(30));
        revokedToken.Revoke(FrozenNow.AddSeconds(-10), RefreshTokenRevokeReason.NORMAL_ROTATION);

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync("hash_of_parallel", Arg.Any<CancellationToken>())
            .Returns(revokedToken);

        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        refreshFactory.ComputeHash("parallel").Returns("hash_of_parallel");

        var handler = CreateHandler(refreshTokens: refreshTokens, refreshFactory: refreshFactory);

        var act = () => handler.Handle(new RefreshCommand("parallel"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_TOKEN_INVALID");

        await refreshTokens.DidNotReceive().RevokeFamilyAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NormalRotationRevokedTokenAfterGrace_RevokesFamilyAndThrows401()
    {
        var user = MakeActiveUser();
        var familyId = Guid.NewGuid();
        var revokedToken = RefreshToken.Create(
            userId: user.Id,
            tokenHash: "hash_of_reused",
            familyId: familyId,
            parentTokenId: null,
            issuedAt: FrozenNow.AddDays(-2),
            expiresAt: FrozenNow.AddDays(28));

        // Mark it as already revoked.
        revokedToken.Revoke(FrozenNow.AddDays(-1), RefreshTokenRevokeReason.NORMAL_ROTATION);

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByTokenHashAsync("hash_of_reused", Arg.Any<CancellationToken>())
            .Returns(revokedToken);

        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        refreshFactory.ComputeHash("reused").Returns("hash_of_reused");
        var refreshTokenFamilyRevoker = Substitute.For<IRefreshTokenFamilyRevoker>();

        var handler = CreateHandler(
            refreshTokens: refreshTokens,
            refreshFactory: refreshFactory,
            refreshTokenFamilyRevoker: refreshTokenFamilyRevoker);

        var act = () => handler.Handle(new RefreshCommand("reused"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_TOKEN_INVALID");

        // Reuse detection must be persisted outside the throwing transaction.
        await refreshTokenFamilyRevoker.Received(1).RevokeForReuseAsync(
            familyId,
            Arg.Any<CancellationToken>());
        await refreshTokens.DidNotReceive().RevokeFamilyAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }
}
