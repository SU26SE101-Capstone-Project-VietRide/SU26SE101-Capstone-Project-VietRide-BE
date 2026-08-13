using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.OperatorUsers.LockOperatorUser;

public sealed class LockOperatorUserCommandHandler
    : IRequestHandler<LockOperatorUserCommand, LockOperatorUserResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public LockOperatorUserCommandHandler(
        IUserRepository users,
        IOperatorRepository operators,
        IRefreshTokenRepository refreshTokens,
        IActivityLogRepository activityLogs,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _users = users;
        _operators = operators;
        _refreshTokens = refreshTokens;
        _activityLogs = activityLogs;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<LockOperatorUserResponseDto> Handle(
        LockOperatorUserCommand request,
        CancellationToken cancellationToken)
    {
        var operatorId = EnsureAuthorized(request.CallerRole, request.CallerOperatorId);
        await EnsureApprovedOperatorAsync(operatorId, cancellationToken);

        var user = await _users.GetManageableOperatorUserForUpdateAsync(
            request.UserId,
            operatorId,
            cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var previousStatus = user.Status;
        var previousLockSource = user.LockSource;
        var statusChanged = user.Lock(UserLockSource.OPERATOR_ADMIN);

        await _refreshTokens.RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.ADMIN_REVOKE,
            cancellationToken);

        var firebaseEvent = new FirebaseSessionRevocationRequestedIntegrationEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            user.Id,
            "USER_LOCKED");
        await _outbox.EnqueueAsync(
            firebaseEvent.EventId,
            FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
            JsonSerializer.Serialize(firebaseEvent),
            cancellationToken);

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.LOCK_USER,
                JsonSerializer.Serialize(new
                {
                    actorUserId = request.CallerUserId,
                    targetUserId = user.Id,
                    operatorId,
                    previousStatus = previousStatus.ToString(),
                    newStatus = user.Status.ToString(),
                    previousLockSource = previousLockSource?.ToString(),
                    newLockSource = user.LockSource?.ToString(),
                    statusChanged,
                    source = "OPERATOR_ADMIN_LOCK_USER",
                }),
                request.IpAddress,
                request.UserAgent),
            cancellationToken);

        return new LockOperatorUserResponseDto(user.Id, user.Status.ToString(), statusChanged);
    }

    private static Guid EnsureAuthorized(string callerRole, Guid? callerOperatorId)
    {
        if (!string.Equals(callerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal)
            || !callerOperatorId.HasValue)
        {
            throw new ForbiddenException("FORBIDDEN", "Only an operator admin can lock operator users.");
        }

        return callerOperatorId.Value;
    }

    private async Task EnsureApprovedOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        var operatorEntity = await _operators.GetByIdNoTrackingAsync(operatorId, cancellationToken);
        if (operatorEntity?.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
            throw new ForbiddenException("FORBIDDEN", "Operator must be approved to lock operator users.");
    }
}
