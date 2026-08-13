using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.OperatorUsers.UnlockOperatorUser;

public sealed class UnlockOperatorUserCommandHandler
    : IRequestHandler<UnlockOperatorUserCommand, UnlockOperatorUserResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly ILoginLockoutCounter _lockoutCounter;
    private readonly IActivityLogRepository _activityLogs;

    public UnlockOperatorUserCommandHandler(
        IUserRepository users,
        IOperatorRepository operators,
        ILoginLockoutCounter lockoutCounter,
        IActivityLogRepository activityLogs)
    {
        _users = users;
        _operators = operators;
        _lockoutCounter = lockoutCounter;
        _activityLogs = activityLogs;
    }

    public async Task<UnlockOperatorUserResponseDto> Handle(
        UnlockOperatorUserCommand request,
        CancellationToken cancellationToken)
    {
        var operatorId = EnsureAuthorized(request.CallerRole, request.CallerOperatorId);
        await EnsureApprovedOperatorAsync(operatorId, cancellationToken);

        var user = await _users.GetManageableOperatorUserForUpdateAsync(
            request.UserId,
            operatorId,
            cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (user.LockSource is not (UserLockSource.OPERATOR_ADMIN or UserLockSource.AUTOMATIC_LOGIN_FAILURE))
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Operator admins cannot unlock a platform or legacy account lock.");
        }

        var previousStatus = user.Status;
        var previousLockSource = user.LockSource;
        await _lockoutCounter.ResetAsync(user.Id, cancellationToken);
        var restoredStatus = user.Unlock();

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.UNLOCK_USER,
                JsonSerializer.Serialize(new
                {
                    actorUserId = request.CallerUserId,
                    targetUserId = user.Id,
                    operatorId,
                    previousStatus = previousStatus.ToString(),
                    newStatus = restoredStatus.ToString(),
                    previousLockSource = previousLockSource?.ToString(),
                    statusChanged = true,
                    source = "OPERATOR_ADMIN_UNLOCK_USER",
                }),
                request.IpAddress,
                request.UserAgent),
            cancellationToken);

        return new UnlockOperatorUserResponseDto(user.Id, restoredStatus.ToString(), true);
    }

    private static Guid EnsureAuthorized(string callerRole, Guid? callerOperatorId)
    {
        if (!string.Equals(callerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal)
            || !callerOperatorId.HasValue)
        {
            throw new ForbiddenException("FORBIDDEN", "Only an operator admin can unlock operator users.");
        }

        return callerOperatorId.Value;
    }

    private async Task EnsureApprovedOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        var operatorEntity = await _operators.GetByIdNoTrackingAsync(operatorId, cancellationToken);
        if (operatorEntity?.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
            throw new ForbiddenException("FORBIDDEN", "Operator must be approved to unlock operator users.");
    }
}
