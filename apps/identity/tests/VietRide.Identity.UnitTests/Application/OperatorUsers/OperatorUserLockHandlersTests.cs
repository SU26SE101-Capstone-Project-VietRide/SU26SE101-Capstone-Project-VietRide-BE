using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.OperatorUsers.LockOperatorUser;
using VietRide.Identity.Application.Features.OperatorUsers.UnlockOperatorUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.OperatorUsers;

public sealed class OperatorUserLockHandlersTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    public async Task Lock_SameTenantManageableRole_LocksAndRevokesSessions(UserRole role)
    {
        var operatorId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var user = ActiveOperatorUser(role, operatorId);
        var users = Substitute.For<IUserRepository>();
        var operators = ApprovedOperatorRepository(operatorId);
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FrozenNow);
        users.GetManageableOperatorUserForUpdateAsync(user.Id, operatorId, Arg.Any<CancellationToken>())
            .Returns(user);
        var handler = new LockOperatorUserCommandHandler(
            users,
            operators,
            refreshTokens,
            activityLogs,
            outbox,
            clock);

        var result = await handler.Handle(
            new LockOperatorUserCommand(
                callerId,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId,
                user.Id,
                "127.0.0.1",
                "tests"),
            CancellationToken.None);

        result.Status.Should().Be(UserStatus.LOCKED.ToString());
        result.StatusChanged.Should().BeTrue();
        user.LockSource.Should().Be(UserLockSource.OPERATOR_ADMIN);
        await refreshTokens.Received(1).RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.ADMIN_REVOKE,
            Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
            Arg.Is<string>(payload => payload.Contains("USER_LOCKED", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log => log.UserId == callerId
                && log.Action == ActivityLogAction.LOCK_USER
                && log.Metadata != null
                && log.Metadata.Contains(operatorId.ToString(), StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_CrossTenantOrUnsupportedRole_ReturnsTenantMaskedNotFound()
    {
        var operatorId = Guid.NewGuid();
        var users = Substitute.For<IUserRepository>();
        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        var handler = new LockOperatorUserCommandHandler(
            users,
            ApprovedOperatorRepository(operatorId),
            refreshTokens,
            Substitute.For<IActivityLogRepository>(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IClock>());

        var act = () => handler.Handle(
            new LockOperatorUserCommand(
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId,
                Guid.NewGuid(),
                null,
                null),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>()
            .Where(exception => exception.EntityName == "User");
        await refreshTokens.DidNotReceive().RevokeActiveByUserAsync(
            Arg.Any<Guid>(),
            Arg.Any<RefreshTokenRevokeReason>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_NonApprovedOperator_IsForbiddenBeforeTargetLookup()
    {
        var operatorId = Guid.NewGuid();
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        operators.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns((Operator?)null);
        var handler = new LockOperatorUserCommandHandler(
            users,
            operators,
            Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<IActivityLogRepository>(),
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IClock>());

        var act = () => handler.Handle(
            new LockOperatorUserCommand(
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId,
                Guid.NewGuid(),
                null,
                null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await users.DidNotReceive().GetManageableOperatorUserForUpdateAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unlock_SystemAdminLock_IsForbiddenAndPreserved()
    {
        var operatorId = Guid.NewGuid();
        var user = ActiveOperatorUser(UserRole.DRIVER, operatorId);
        user.Lock(UserLockSource.SYSTEM_ADMIN);
        var users = Substitute.For<IUserRepository>();
        users.GetManageableOperatorUserForUpdateAsync(user.Id, operatorId, Arg.Any<CancellationToken>())
            .Returns(user);
        var counter = Substitute.For<ILoginLockoutCounter>();
        var handler = new UnlockOperatorUserCommandHandler(
            users,
            ApprovedOperatorRepository(operatorId),
            counter,
            Substitute.For<IActivityLogRepository>());

        var act = () => handler.Handle(
            new UnlockOperatorUserCommand(
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId,
                user.Id,
                null,
                null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        user.Status.Should().Be(UserStatus.LOCKED);
        user.LockSource.Should().Be(UserLockSource.SYSTEM_ADMIN);
        await counter.DidNotReceive().ResetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UserLockSource.OPERATOR_ADMIN)]
    [InlineData(UserLockSource.AUTOMATIC_LOGIN_FAILURE)]
    public async Task Unlock_OperatorOrAutomaticLock_RestoresActive(UserLockSource source)
    {
        var operatorId = Guid.NewGuid();
        var user = ActiveOperatorUser(UserRole.ASSISTANT, operatorId);
        if (source == UserLockSource.OPERATOR_ADMIN)
        {
            user.Lock(source);
        }
        else
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(FrozenNow);
            user.RecordFailedLogin(clock, 5);
        }

        var users = Substitute.For<IUserRepository>();
        users.GetManageableOperatorUserForUpdateAsync(user.Id, operatorId, Arg.Any<CancellationToken>())
            .Returns(user);
        var counter = Substitute.For<ILoginLockoutCounter>();
        var handler = new UnlockOperatorUserCommandHandler(
            users,
            ApprovedOperatorRepository(operatorId),
            counter,
            Substitute.For<IActivityLogRepository>());

        var result = await handler.Handle(
            new UnlockOperatorUserCommand(
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId,
                user.Id,
                null,
                null),
            CancellationToken.None);

        result.Status.Should().Be(UserStatus.ACTIVE.ToString());
        user.LockSource.Should().BeNull();
        await counter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    private static IOperatorRepository ApprovedOperatorRepository(Guid operatorId)
    {
        var repository = Substitute.For<IOperatorRepository>();
        repository.GetByIdNoTrackingAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(Operator.CreateApproved(
                "Operator",
                "BRN-123",
                "TAX-123",
                "operator@example.com",
                "+84901234567",
                Guid.NewGuid(),
                FrozenNow));
        return repository;
    }

    private static User ActiveOperatorUser(UserRole role, Guid operatorId)
    {
        var user = User.CreateOperatorScopedPendingPassword(
            $"{role.ToString().ToLowerInvariant()}@example.com",
            PhoneNumber.Parse(role == UserRole.DRIVER ? "+84901234567" : "+84901234568"),
            role.ToString(),
            role,
            operatorId);
        user.SetInitialPassword("old-hash");
        return user;
    }
}
