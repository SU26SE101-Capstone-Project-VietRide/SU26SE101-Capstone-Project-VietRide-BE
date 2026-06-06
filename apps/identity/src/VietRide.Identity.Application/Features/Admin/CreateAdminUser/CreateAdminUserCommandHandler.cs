using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.CreateAdminUser;

public sealed class CreateAdminUserCommandHandler : IRequestHandler<CreateAdminUserCommand, CreateAdminUserResponseDto>
{
    private const string SetInitialPasswordUrlBase = "https://app.vietride.app/auth/set-password?token=";

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IInitialPasswordTokenService _initialPasswordTokens;
    private readonly IEmailService _emailService;
    private readonly IClock _clock;

    public CreateAdminUserCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IActivityLogRepository activityLogs,
        IInitialPasswordTokenService initialPasswordTokens,
        IEmailService emailService,
        IClock clock)
    {
        _users = users;
        _tokens = tokens;
        _activityLogs = activityLogs;
        _initialPasswordTokens = initialPasswordTokens;
        _emailService = emailService;
        _clock = clock;
    }

    public async Task<CreateAdminUserResponseDto> Handle(
        CreateAdminUserCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can create admin users.");

        if (!string.Equals(request.Role, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
        {
            throw new ValidationException(
                "Only SYSTEM_ADMIN can be created by this endpoint.",
                [new ValidationError("role", "Only SYSTEM_ADMIN can be created by this endpoint.")]);
        }

        var emailLower = request.Email.Trim().ToLowerInvariant();
        var existing = await _users.GetByEmailAsync(emailLower, cancellationToken);
        if (existing is not null)
            throw new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email is already registered.");

        var user = User.CreateAdminPendingPassword(
            email: emailLower,
            displayName: request.DisplayName.Trim());

        await _users.AddAsync(user, cancellationToken);

        var now = _clock.UtcNow;
        var code = _initialPasswordTokens.GenerateCode();
        var expiresAt = _initialPasswordTokens.GetExpiresAt(now);
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            code,
            expiresAt);

        await _tokens.AddAsync(token, cancellationToken);

        var setInitialPasswordUrl = SetInitialPasswordUrlBase + code;
        await _emailService.SendAccountCreatedLinkAsync(
            user.Email,
            new AccountCreatedEmailDto(user.Id, user.DisplayName, setInitialPasswordUrl, expiresAt),
            cancellationToken);

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                user.Id,
                ActivityLogAction.SET_INITIAL_PASSWORD,
                $"{{\"callerUserId\":\"{request.CallerUserId}\"}}"),
            cancellationToken);

        return new CreateAdminUserResponseDto(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Role: user.Role.ToString(),
            Status: user.Status.ToString());
    }
}
