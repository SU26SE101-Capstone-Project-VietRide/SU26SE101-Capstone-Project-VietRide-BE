using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Admin.UnlockUser;

public sealed class UnlockUserCommandHandler : IRequestHandler<UnlockUserCommand, UnlockUserResponseDto>
{
    private readonly IUserRepository _users;
    private readonly ILoginLockoutCounter _lockoutCounter;
    private readonly IActivityLogRepository _activityLogs;

    public UnlockUserCommandHandler(
        IUserRepository users,
        ILoginLockoutCounter lockoutCounter,
        IActivityLogRepository activityLogs)
    {
        _users = users;
        _lockoutCounter = lockoutCounter;
        _activityLogs = activityLogs;
    }

    public async Task<UnlockUserResponseDto> Handle(
        UnlockUserCommand request,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized(request.CallerUserId, request.CallerRole, request.UserId);

        var user = await _users.GetByIdForUpdateAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        var previousStatus = user.Status;

        await _lockoutCounter.ResetAsync(user.Id, cancellationToken);
        var restoredStatus = user.Unlock();

        var metadata = JsonSerializer.Serialize(new
        {
            targetUserId = user.Id,
            previousStatus = previousStatus.ToString(),
            newStatus = restoredStatus.ToString(),
            statusChanged = true,
        });
        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.UNLOCK_USER,
                metadata,
                request.IpAddress,
                request.UserAgent),
            cancellationToken);

        return new UnlockUserResponseDto(user.Id, restoredStatus.ToString(), true);
    }

    private static void EnsureAuthorized(Guid callerUserId, string callerRole, Guid targetUserId)
    {
        if (!string.Equals(callerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can unlock users.");

        if (callerUserId == targetUserId)
            throw new ForbiddenException("FORBIDDEN", "A SYSTEM_ADMIN cannot unlock itself.");
    }
}
