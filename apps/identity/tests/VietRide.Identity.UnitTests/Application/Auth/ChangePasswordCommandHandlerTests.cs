using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Auth.ChangePassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ChangePasswordCommandHandlerTests
{
    [Theory]
    [InlineData(UserRole.PASSENGER)]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    [InlineData(UserRole.OPERATOR_STAFF)]
    [InlineData(UserRole.OPERATOR_ADMIN)]
    [InlineData(UserRole.SYSTEM_ADMIN)]
    public async Task Handle_ActiveLocalPasswordUser_ChangesPasswordAndRevokesEverySession(UserRole role)
    {
        var user = ActiveUser(role);
        var users = Substitute.For<IUserRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var counter = Substitute.For<ILoginLockoutCounter>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        hasher.Verify("OldPassword1", "old-hash").Returns(true);
        hasher.Verify("NewPassword2", "old-hash").Returns(false);
        hasher.Hash("NewPassword2").Returns("new-hash");
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var handler = new ChangePasswordCommandHandler(
            users,
            hasher,
            refreshTokens,
            counter,
            activityLogs,
            outbox,
            clock);

        var result = await handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPassword1", "NewPassword2", null, null),
            CancellationToken.None);

        result.Should().Be(new ChangePasswordResponseDto(user.Id, true));
        user.PasswordHash.Should().Be("new-hash");
        await counter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
        await refreshTokens.Received(1).RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.PASSWORD_CHANGE,
            Arg.Any<CancellationToken>());
        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log => log.UserId == user.Id && log.Action == ActivityLogAction.CHANGE_PASSWORD),
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
            Arg.Is<string>(payload => payload.Contains("PASSWORD_CHANGED", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GoogleOnlyOrWrongCurrentPassword_ReturnsUniformInvalidCredentials()
    {
        var user = User.CreateGoogleAccount("google@example.com", "Google", null);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = BuildHandler(users, Substitute.For<IPasswordHasher>());

        var act = () => handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPassword1", "NewPassword2", null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(exception => exception.ErrorCode == "AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Handle_NewPasswordMatchesCurrent_ReturnsValidationErrorWithoutMutation()
    {
        var user = ActiveUser(UserRole.PASSENGER);
        var users = Substitute.For<IUserRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        hasher.Verify("OldPassword1", "old-hash").Returns(true);
        hasher.Verify("SamePassword1", "old-hash").Returns(true);
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var handler = BuildHandler(users, hasher, refreshTokens);

        var act = () => handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPassword1", "SamePassword1", null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        user.PasswordHash.Should().Be("old-hash");
        await refreshTokens.DidNotReceive().RevokeActiveByUserAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }

    private static ChangePasswordCommandHandler BuildHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IRefreshTokenRepository? refreshTokens = null)
        => new(
            users,
            hasher,
            refreshTokens ?? Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<ILoginLockoutCounter>(),
            Substitute.For<IActivityLogRepository>(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IClock>());

    private static User ActiveUser(UserRole role)
    {
        var user = role switch
        {
            UserRole.PASSENGER => User.CreatePassenger(
                "passenger@example.com",
                PhoneNumber.Parse("+84901234567"),
                "old-hash",
                "Passenger"),
            UserRole.SYSTEM_ADMIN => User.CreateAdminPendingPassword("admin@example.com", "Admin"),
            UserRole.OPERATOR_ADMIN => User.CreateOperatorAdminPendingPassword(
                "operator-admin@example.com",
                PhoneNumber.Parse("+84901234568"),
                "Operator Admin",
                Guid.NewGuid()),
            _ => User.CreateOperatorScopedPendingPassword(
                $"{role.ToString().ToLowerInvariant()}@example.com",
                PhoneNumber.Parse("+84901234569"),
                role.ToString(),
                role,
                Guid.NewGuid()),
        };

        if (user.Status == UserStatus.PENDING_EMAIL_VERIFICATION)
            user.VerifyEmail();
        else
            user.SetInitialPassword("old-hash");

        return user;
    }
}
