using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.ResendInitialPassword;

public sealed class ResendInitialPasswordCommandHandler
    : IRequestHandler<ResendInitialPasswordCommand, ResendInitialPasswordResponseDto>
{

    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IInitialPasswordTokenService _initialPasswordTokens;
    private readonly IEmailService _emailService;
    private readonly IClock _clock;

    public ResendInitialPasswordCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IActivityLogRepository activityLogs,
        IInitialPasswordTokenService initialPasswordTokens,
        IEmailService emailService,
        IClock clock,
        IOperatorRepository operators)
    {
        _users = users;
        _operators = operators;
        _tokens = tokens;
        _activityLogs = activityLogs;
        _initialPasswordTokens = initialPasswordTokens;
        _emailService = emailService;
        _clock = clock;
    }

    public async Task<ResendInitialPasswordResponseDto> Handle(
        ResendInitialPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can resend initial-password links.");

        if (!request.CallerOperatorId.HasValue)
            throw new ForbiddenException("FORBIDDEN", "Operator scope is required to resend initial-password links.");

        var operatorEntity = await _operators.GetByIdAsync(request.CallerOperatorId.Value, cancellationToken);
        if (operatorEntity?.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
            throw new ForbiddenException("FORBIDDEN", "Operator must be approved to resend initial-password links.");

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        EnsureOperatorScopedTarget(user, request.CallerOperatorId.Value);

        if (user.Status != UserStatus.PENDING_INITIAL_PASSWORD)
        {
            throw new IdentityDomainException(
                "USER_INVALID_STATUS_TRANSITION",
                "Initial-password link can only be resent for users pending initial password setup.");
        }

        var now = _clock.UtcNow;
        await _tokens.RevokeActiveByUserAndPurposeAsync(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            now,
            cancellationToken);

        var code = _initialPasswordTokens.GenerateCode();
        var expiresAt = _initialPasswordTokens.GetExpiresAt(now);
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            code,
            expiresAt);

        await _tokens.AddAsync(token, cancellationToken);

        var setInitialPasswordUrl = _initialPasswordTokens.BuildSetInitialPasswordUrl(code);
        await _emailService.SendAccountCreatedLinkAsync(
            user.Email,
            new AccountCreatedEmailDto(user.Id, user.DisplayName, setInitialPasswordUrl, expiresAt),
            cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            operatorId = request.CallerOperatorId.Value,
            actorUserId = request.CallerUserId,
            callerUserId = request.CallerUserId,
            targetUserId = user.Id,
            source = "RESEND_INITIAL_PASSWORD",
        });

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.RESEND_INITIAL_PASSWORD,
                metadata),
            cancellationToken);

        return new ResendInitialPasswordResponseDto(user.Id, user.Status.ToString(), expiresAt);
    }

    private static void EnsureOperatorScopedTarget(User user, Guid callerOperatorId)
    {
        if (user.Role is UserRole.PASSENGER or UserRole.SYSTEM_ADMIN || !user.OperatorId.HasValue)
            throw new ForbiddenException("FORBIDDEN", "Target user is not scoped to an operator.");

        if (user.OperatorId.Value != callerOperatorId)
            throw new ForbiddenException("FORBIDDEN", "Target user belongs to another operator.");
    }
}
