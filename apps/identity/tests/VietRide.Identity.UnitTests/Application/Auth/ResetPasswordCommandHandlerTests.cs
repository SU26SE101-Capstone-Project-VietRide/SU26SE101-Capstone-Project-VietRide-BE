using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.ResetPassword;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ResetPasswordCommandHandlerTests
{
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordResetSessionExecutor _sessionExecutor = Substitute.For<IPasswordResetSessionExecutor>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();

    public ResetPasswordCommandHandlerTests()
    {
        _passwordHasher.Hash("NewPassword123").Returns("new-hash");
    }

    [Fact]
    public async Task Handle_ValidOtp_ReturnsCommittedSessionResult()
    {
        var user = CreateActivePassenger();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _sessionExecutor.ExecuteAsync(user.Id, "123456", "new-hash", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetSessionResult(
                PasswordResetSessionStatus.SUCCEEDED,
                user.Id,
                UserStatus.ACTIVE.ToString()));
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        result.UserId.Should().Be(user.Id);
        result.Status.Should().Be(UserStatus.ACTIVE.ToString());
        await _sessionExecutor.Received(1).ExecuteAsync(
            user.Id,
            "123456",
            "new-hash",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongOtp_IncrementsFailedAttemptAndThrowsAuthOtpInvalid()
    {
        var user = CreateActivePassenger();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _sessionExecutor.ExecuteAsync(user.Id, "000000", "new-hash", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetSessionResult(PasswordResetSessionStatus.INVALID_OTP));
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "000000", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await _sessionExecutor.Received(1).ExecuteAsync(
            user.Id,
            "000000",
            "new-hash",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpiredOtp_ThrowsAuthOtpExpired()
    {
        var user = CreateActivePassenger();
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _sessionExecutor.ExecuteAsync(user.Id, "123456", "new-hash", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetSessionResult(PasswordResetSessionStatus.EXPIRED_OTP));
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_EXPIRED");
    }

    [Fact]
    public async Task Handle_NonActiveUser_ThrowsAuthOtpInvalid()
    {
        var user = VietRide.Identity.Domain.Entities.User.CreatePassenger("pending@example.com", TestPhone, "old-hash", "Pending");
        _users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _sessionExecutor.ExecuteAsync(user.Id, "123456", "new-hash", Arg.Any<CancellationToken>())
            .Returns(new PasswordResetSessionResult(PasswordResetSessionStatus.INVALID_OTP));
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new ResetPasswordCommand(user.Email, "123456", "NewPassword123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await _sessionExecutor.Received(1).ExecuteAsync(
            user.Id,
            "123456",
            "new-hash",
            Arg.Any<CancellationToken>());
    }

    private ResetPasswordCommandHandler CreateHandler()
        => new(
            _users,
            _sessionExecutor,
            _passwordHasher);

    private static VietRide.Identity.Domain.Entities.User CreateActivePassenger()
    {
        var user = VietRide.Identity.Domain.Entities.User.CreatePassenger("passenger@example.com", TestPhone, "old-hash", "Passenger");
        user.VerifyEmail();
        return user;
    }
}
