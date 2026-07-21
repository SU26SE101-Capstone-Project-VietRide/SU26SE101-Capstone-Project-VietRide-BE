using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.LockUser;

public sealed class LockUserCommandHandler : IRequestHandler<LockUserCommand, LockUserResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;

    public LockUserCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IActivityLogRepository activityLogs,
        IClock clock,
        IIntegrationEventOutbox outbox)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _activityLogs = activityLogs;
        _clock = clock;
        _outbox = outbox;
    }

    public async Task<LockUserResponseDto> Handle(
        LockUserCommand request,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized(request.CallerUserId, request.CallerRole, request.UserId);

        var user = await _users.GetByIdForUpdateAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        var previousStatus = user.Status;
        var statusChanged = user.Lock();

        if (!statusChanged
            && user.LockedFromStatus is not (UserStatus.ACTIVE or UserStatus.PENDING_EMAIL_VERIFICATION))
        {
            throw new InvalidOperationException("LOCKED user is missing a valid lockedFromStatus invariant.");
        }

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

        var metadata = JsonSerializer.Serialize(new
        {
            targetUserId = user.Id,
            previousStatus = previousStatus.ToString(),
            newStatus = user.Status.ToString(),
            statusChanged,
        });
        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.LOCK_USER,
                metadata,
                request.IpAddress,
                request.UserAgent),
            cancellationToken);

        return new LockUserResponseDto(user.Id, user.Status.ToString(), statusChanged);
    }

    private static void EnsureAuthorized(Guid callerUserId, string callerRole, Guid targetUserId)
    {
        if (!string.Equals(callerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can lock users.");

        if (callerUserId == targetUserId)
            throw new ForbiddenException("FORBIDDEN", "A SYSTEM_ADMIN cannot lock itself.");
    }
}
