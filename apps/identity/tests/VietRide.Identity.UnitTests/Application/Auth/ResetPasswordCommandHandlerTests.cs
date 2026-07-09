using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.ResetPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ResetPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailVerificationTokenRepository _tokens = Substitute.For<IEmailVerificationTokenRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IOtpFailedAttemptPersister _failedAttemptPersister = Substitute.For<IOtpFailedAttemptPersister>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ResetPasswordCommandHandlerTests()
    {
        _clock.UtcNow.Returns(FrozenNow);
        _passwordHasher.Hash("NewPassword123").Returns("new-hash");
    }

    [Fact]
    public async Task Handle_ValidOtp_ResetsPasswordMarksOtpUsedAndRevokesRefreshTokens()
    {
        var user = CreateActivePassenger();
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.PASSWORD_RESET,
            "123456",
            FrozenNow.AddMinutes(5));
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _tokens.FindByCodeAsync(user.Id, "123456", EmailVerificationPurpose.PASSWORD_RESET, Arg.Any<CancellationToken>())
            .Returns(token);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        result.UserId.Should().Be(user.Id);
        result.Status.Should().Be(UserStatus.ACTIVE.ToString());
        user.PasswordHash.Should().Be("new-hash");
        token.UsedAt.Should().Be(FrozenNow);
        _tokens.Received(1).Update(token);
        await _refreshTokens.Received(1).RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.PASSWORD_RESET,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongOtp_IncrementsFailedAttemptAndThrowsAuthOtpInvalid()
    {
        var user = CreateActivePassenger();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _tokens.FindByCodeAsync(user.Id, "000000", EmailVerificationPurpose.PASSWORD_RESET, Arg.Any<CancellationToken>())
            .Returns((EmailVerificationToken?)null);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "000000", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await _failedAttemptPersister.Received(1).PersistAsync(
            user.Id,
            EmailVerificationPurpose.PASSWORD_RESET,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpiredOtp_ThrowsAuthOtpExpired()
    {
        var user = CreateActivePassenger();
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.PASSWORD_RESET,
            "123456",
            FrozenNow.AddSeconds(-1));
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _tokens.FindByCodeAsync(user.Id, "123456", EmailVerificationPurpose.PASSWORD_RESET, Arg.Any<CancellationToken>())
            .Returns(token);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_EXPIRED");
        token.UsedAt.Should().BeNull();
        await _refreshTokens.DidNotReceive().RevokeActiveByUserAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonActiveUser_ThrowsAuthOtpInvalid()
    {
        var user = User.CreatePassenger("pending@example.com", TestPhone, "old-hash", "Pending");
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await _tokens.DidNotReceive().FindByCodeAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<EmailVerificationPurpose>(),
            Arg.Any<CancellationToken>());
    }

    private ResetPasswordCommandHandler CreateHandler()
        => new(
            _users,
            _tokens,
            _refreshTokens,
            _failedAttemptPersister,
            _passwordHasher,
            _clock);

    private static User CreateActivePassenger()
    {
        var user = User.CreatePassenger("passenger@example.com", TestPhone, "old-hash", "Passenger");
        user.VerifyEmail();
        return user;
    }
}
