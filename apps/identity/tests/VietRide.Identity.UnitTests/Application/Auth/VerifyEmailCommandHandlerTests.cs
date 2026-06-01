using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.VerifyEmail;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class VerifyEmailCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private static VerifyEmailCommandHandler BuildHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IClock clock)
        => new(users, tokens, clock);

    private static User MakePendingUser(string email = "user@example.com")
        => User.CreatePassenger(email, TestPhone, "hash", "Test User");

    private static EmailVerificationToken MakeToken(
        Guid userId,
        string code = "123456",
        EmailVerificationPurpose purpose = EmailVerificationPurpose.REGISTRATION,
        int failedAttempts = 0,
        DateTimeOffset? expiresAt = null)
    {
        var token = EmailVerificationToken.Create(
            userId,
            purpose,
            code,
            expiresAt ?? FrozenNow.AddMinutes(5));

        for (var i = 0; i < failedAttempts; i++)
            token.IncrementFailedAttempts();

        return token;
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_CorrectCode_Returns200AndActiveStatus()
    {
        var user = MakePendingUser();
        var token = MakeToken(user.Id);

        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        tokens.FindByCodeAsync(user.Id, "123456", EmailVerificationPurpose.REGISTRATION, Arg.Any<CancellationToken>())
            .Returns(token);

        var handler = BuildHandler(users, tokens, clock);

        var result = await handler.Handle(
            new VerifyEmailCommand("user@example.com", "123456", "REGISTRATION"),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.UserId.Should().Be(user.Id);
        result.Status.Should().Be(UserStatus.ACTIVE.ToString());
        token.UsedAt.Should().Be(FrozenNow);
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WrongCode_Throws400OtpInvalid_AndIncrementsLatestPending()
    {
        var user = MakePendingUser();
        var latestPending = MakeToken(user.Id, code: "654321"); // different code

        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        // FindByCodeAsync returns null — no exact match (wrong code).
        tokens.FindByCodeAsync(user.Id, "000000", EmailVerificationPurpose.REGISTRATION, Arg.Any<CancellationToken>())
            .Returns((EmailVerificationToken?)null);
        // FindLatestPendingAsync returns the outstanding token.
        tokens.FindLatestPendingAsync(user.Id, EmailVerificationPurpose.REGISTRATION, Arg.Any<CancellationToken>())
            .Returns(latestPending);

        var handler = BuildHandler(users, tokens, clock);

        var act = () => handler.Handle(
            new VerifyEmailCommand("user@example.com", "000000", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");

        // failed_attempts must have been incremented on the latest pending token.
        latestPending.FailedAttempts.Should().Be(1);
        tokens.Received(1).Update(latestPending);
    }

    [Fact]
    public async Task Handle_ExpiredCode_Throws400OtpExpired_DoesNotIncrement()
    {
        var user = MakePendingUser();
        // Token already expired (expiresAt in the past).
        var expiredToken = MakeToken(user.Id, expiresAt: FrozenNow.AddMinutes(-1));

        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        tokens.FindByCodeAsync(user.Id, "123456", EmailVerificationPurpose.REGISTRATION, Arg.Any<CancellationToken>())
            .Returns(expiredToken);

        var handler = BuildHandler(users, tokens, clock);

        var act = () => handler.Handle(
            new VerifyEmailCommand("user@example.com", "123456", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_EXPIRED");

        // Expiry must NOT increment failed_attempts.
        expiredToken.FailedAttempts.Should().Be(0);
        tokens.DidNotReceive().Update(expiredToken);
    }

    [Fact]
    public async Task Handle_BurnedToken_FailedAttemptsGte5_Throws400OtpInvalid()
    {
        var user = MakePendingUser();
        // Token has already reached the 5-attempt limit.
        var burnedToken = MakeToken(user.Id, failedAttempts: 5);

        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        tokens.FindByCodeAsync(user.Id, "123456", EmailVerificationPurpose.REGISTRATION, Arg.Any<CancellationToken>())
            .Returns(burnedToken);

        var handler = BuildHandler(users, tokens, clock);

        var act = () => handler.Handle(
            new VerifyEmailCommand("user@example.com", "123456", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
    }

    [Fact]
    public async Task Handle_UserNotFound_Throws400OtpInvalid()
    {
        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = BuildHandler(users, tokens, clock);

        var act = () => handler.Handle(
            new VerifyEmailCommand("nobody@example.com", "123456", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
    }

    [Fact]
    public async Task Handle_InvalidPurpose_Throws400OtpInvalid_DoesNotEchoInput()
    {
        var user = MakePendingUser();

        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var handler = BuildHandler(users, tokens, clock);

        var act = () => handler.Handle(
            new VerifyEmailCommand("user@example.com", "123456", "INVALID_PURPOSE"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.ErrorCode.Should().Be("AUTH_OTP_INVALID");
        // SF3: message must not echo user-supplied purpose value.
        ex.Which.Message.Should().NotContain("INVALID_PURPOSE");
    }
}
