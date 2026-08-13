using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.ChangePassword;

public sealed class ChangePasswordCommandHandler
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILoginLockoutCounter _lockoutCounter;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ChangePasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IRefreshTokenRepository refreshTokens,
        ILoginLockoutCounter lockoutCounter,
        IActivityLogRepository activityLogs,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _refreshTokens = refreshTokens;
        _lockoutCounter = lockoutCounter;
        _activityLogs = activityLogs;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ChangePasswordResponseDto> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdForUpdateAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (user.Status != UserStatus.ACTIVE)
        {
            throw new InvalidUserStatusTransitionException(
                user.Status.ToString(),
                UserStatus.ACTIVE.ToString());
        }

        if (user.PasswordHash is null
            || !_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedException("AUTH_INVALID_CREDENTIALS", "Current password is incorrect.");
        }

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            throw new ValidationException(
                "New password must be different from the current password.",
                [new ValidationError("newPassword", "New password must be different from the current password.")]);
        }

        await _lockoutCounter.ResetAsync(user.Id, cancellationToken);
        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));

        await _refreshTokens.RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.PASSWORD_CHANGE,
            cancellationToken);

        var now = _clock.UtcNow;
        var firebaseEvent = new FirebaseSessionRevocationRequestedIntegrationEvent(
            Guid.NewGuid(),
            now,
            user.Id,
            "PASSWORD_CHANGED");
        await _outbox.EnqueueAsync(
            firebaseEvent.EventId,
            FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
            JsonSerializer.Serialize(firebaseEvent),
            cancellationToken);

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                user.Id,
                ActivityLogAction.CHANGE_PASSWORD,
                JsonSerializer.Serialize(new
                {
                    actorUserId = user.Id,
                    targetUserId = user.Id,
                    source = "SELF_CHANGE_PASSWORD",
                }),
                request.IpAddress,
                request.UserAgent),
            cancellationToken);

        return new ChangePasswordResponseDto(user.Id, SessionsRevoked: true);
    }
}
