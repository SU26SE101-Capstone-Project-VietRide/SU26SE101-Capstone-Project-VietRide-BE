using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Admin.ListUsers;
using VietRide.Identity.Application.Features.Admin.LockUser;
using VietRide.Identity.Application.Features.Admin.UnlockUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.AdminUsers;

public sealed class AdminUserHandlersTests
{
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    [Fact]
    public async Task List_MapsOnlyPublicAdminFieldsAndForwardsFilters()
    {
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        users.ListAdminUsersAsync(
                Arg.Any<QueryOptions>(),
                UserRole.PASSENGER,
                UserStatus.ACTIVE,
                null,
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<User>.Create([user], 1, 20, 1));
        var handler = new ListUsersQueryHandler(users);

        var result = await handler.Handle(
            new ListUsersQuery(
                UserRole.SYSTEM_ADMIN.ToString(),
                "passenger",
                UserRole.PASSENGER.ToString(),
                UserStatus.ACTIVE.ToString(),
                null,
                false,
                1,
                20,
                "createdAt",
                "desc"),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Should().BeEquivalentTo(new AdminUserListItemDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Phone?.Value,
            user.AvatarUrl,
            user.Role.ToString(),
            user.Status.ToString(),
            user.OperatorId,
            user.CreatedAt,
            user.UpdatedAt,
            user.DeletedAt));
        typeof(AdminUserListItemDto).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("OAuth", StringComparison.OrdinalIgnoreCase)
                || name.Contains("FailedLogin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_UnsupportedSort_ReturnsCanonicalBadRequest()
    {
        var handler = new ListUsersQueryHandler(Substitute.For<IUserRepository>());

        var act = () => handler.Handle(
            new ListUsersQuery(
                UserRole.SYSTEM_ADMIN.ToString(),
                null,
                null,
                null,
                null,
                false,
                1,
                20,
                "passwordHash",
                "asc"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(exception => exception.ErrorCode == "INVALID_SORT_FIELD");
    }

    [Fact]
    public async Task Lock_ActiveUser_RevokesRefreshTokensAndWritesAllowListedAudit()
    {
        var callerId = Guid.NewGuid();
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        activityLogs.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ActivityLog>());
        var handler = new LockUserCommandHandler(users, refreshTokens, activityLogs, clock, outbox);

        var result = await handler.Handle(
            new LockUserCommand(callerId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, "127.0.0.1", "tests"),
            CancellationToken.None);

        result.Status.Should().Be(UserStatus.LOCKED.ToString());
        result.StatusChanged.Should().BeTrue();
        user.LockedFromStatus.Should().Be(UserStatus.ACTIVE);
        await refreshTokens.Received(1).RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.ADMIN_REVOKE,
            Arg.Any<CancellationToken>());
        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log => log.UserId == callerId
                && log.Action == ActivityLogAction.LOCK_USER
                && log.Metadata != null
                && log.Metadata.Contains("targetUserId", StringComparison.Ordinal)
                && !log.Metadata.Contains("password", StringComparison.OrdinalIgnoreCase)
                && !log.Metadata.Contains("token", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
            Arg.Is<string>(payload => payload.Contains(user.Id.ToString(), StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_AlreadyLocked_EnsuresRevocationAndReturnsStatusChangedFalse()
    {
        var user = ActivePassenger();
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LockUserCommandHandler(users, refreshTokens, activityLogs, clock, outbox);

        var result = await handler.Handle(
            new LockUserCommand(Guid.NewGuid(), UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            CancellationToken.None);

        result.StatusChanged.Should().BeFalse();
        await refreshTokens.Received(1).RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.ADMIN_REVOKE,
            Arg.Any<CancellationToken>());
        await activityLogs.Received(1).AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_AutomaticLock_EscalatesSourceToSystemAdmin()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var user = ActivePassenger();
        user.RecordFailedLogin(clock, 5);
        user.LockSource.Should().Be(UserLockSource.AUTOMATIC_LOGIN_FAILURE);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new LockUserCommandHandler(
            users,
            Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<IActivityLogRepository>(),
            clock,
            Substitute.For<IIntegrationEventOutbox>());

        var result = await handler.Handle(
            new LockUserCommand(
                Guid.NewGuid(),
                UserRole.SYSTEM_ADMIN.ToString(),
                user.Id,
                null,
                null),
            CancellationToken.None);

        result.StatusChanged.Should().BeFalse();
        user.Status.Should().Be(UserStatus.LOCKED);
        user.LockSource.Should().Be(UserLockSource.SYSTEM_ADMIN);
    }

    [Fact]
    public async Task Unlock_PendingEmailLockout_RestoresPendingAndResetsRedis()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var user = User.CreatePassenger("pending@example.com", TestPhone, "hash", "Pending");
        user.RecordFailedLogin(clock, 5);
        var users = Substitute.For<IUserRepository>();
        var counter = Substitute.For<ILoginLockoutCounter>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new UnlockUserCommandHandler(users, counter, activityLogs);

        var result = await handler.Handle(
            new UnlockUserCommand(Guid.NewGuid(), UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            CancellationToken.None);

        result.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION.ToString());
        user.LockedFromStatus.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
        await counter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unlock_RedisFailure_DoesNotMutateUserOrWriteAudit()
    {
        var user = ActivePassenger();
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        var counter = Substitute.For<ILoginLockoutCounter>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        users.GetByIdForUpdateAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        counter.ResetAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Redis unavailable"));
        var handler = new UnlockUserCommandHandler(users, counter, activityLogs);

        var act = () => handler.Handle(
            new UnlockUserCommand(Guid.NewGuid(), UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        user.Status.Should().Be(UserStatus.LOCKED);
        user.LockedFromStatus.Should().Be(UserStatus.ACTIVE);
        await activityLogs.DidNotReceive().AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockUnlock_SelfAction_IsForbidden(bool lockAction)
    {
        var callerId = Guid.NewGuid();
        Func<Task> act = lockAction
            ? () => new LockUserCommandHandler(
                    Substitute.For<IUserRepository>(),
                    Substitute.For<IRefreshTokenRepository>(),
                    Substitute.For<IActivityLogRepository>(),
                    Substitute.For<IClock>(),
                    Substitute.For<IIntegrationEventOutbox>())
                .Handle(
                    new LockUserCommand(callerId, UserRole.SYSTEM_ADMIN.ToString(), callerId, null, null),
                    CancellationToken.None)
            : () => new UnlockUserCommandHandler(
                    Substitute.For<IUserRepository>(),
                    Substitute.For<ILoginLockoutCounter>(),
                    Substitute.For<IActivityLogRepository>())
                .Handle(
                    new UnlockUserCommand(callerId, UserRole.SYSTEM_ADMIN.ToString(), callerId, null, null),
                    CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
    }

    private static User ActivePassenger()
    {
        var user = User.CreatePassenger("passenger@example.com", TestPhone, "hash", "Passenger");
        user.VerifyEmail();
        return user;
    }
}
