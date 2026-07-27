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
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;

public sealed class CreateOperatorUserCommandHandler
    : IRequestHandler<CreateOperatorUserCommand, CreateOperatorUserResponseDto>
{

    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IInitialPasswordTokenService _initialPasswordTokens;
    private readonly IEmailService _emailService;
    private readonly IClock _clock;
    private readonly ISubscriptionUsageWarningPublisher _usageWarnings;

    public CreateOperatorUserCommandHandler(
        IUserRepository users,
        IOperatorRepository operators,
        IOperatorSubscriptionRepository subscriptions,
        IInitialPasswordTokenService initialPasswordTokens,
        IEmailService emailService,
        IClock clock,
        ISubscriptionUsageWarningPublisher usageWarnings)
    {
        _users = users;
        _operators = operators;
        _subscriptions = subscriptions;
        _initialPasswordTokens = initialPasswordTokens;
        _emailService = emailService;
        _clock = clock;
        _usageWarnings = usageWarnings;
    }

    public async Task<CreateOperatorUserResponseDto> Handle(
        CreateOperatorUserCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can create operator-scoped users.");

        if (!request.CallerOperatorId.HasValue)
            throw new ForbiddenException("FORBIDDEN", "Operator scope is required to create operator-scoped users.");

        var operatorEntity = await _operators.GetByIdAsync(request.CallerOperatorId.Value, cancellationToken);
        if (operatorEntity?.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
            throw new ForbiddenException("FORBIDDEN", "Operator must be approved to create operator-scoped users.");

        var role = Enum.Parse<UserRole>(request.Role, ignoreCase: false);
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(request.Phone);
        var displayName = request.DisplayName.Trim();

        if (await _users.GetByEmailAsync(email, cancellationToken) is not null)
            throw new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email is already registered.");

        if (await _users.GetByPhoneAsync(phone.Value, cancellationToken) is not null)
            throw new ConflictException("AUTH_PHONE_ALREADY_REGISTERED", "Phone number is already registered.");

        var user = User.CreateOperatorScopedPendingPassword(
            email,
            phone,
            displayName,
            role,
            request.CallerOperatorId.Value);

        var now = _clock.UtcNow;
        var code = _initialPasswordTokens.GenerateCode();
        var expiresAt = _initialPasswordTokens.GetExpiresAt(now);
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            code,
            expiresAt);
        var metadata = JsonSerializer.Serialize(new
        {
            actorUserId = request.CallerUserId,
            callerUserId = request.CallerUserId,
            targetUserId = user.Id,
            operatorId = request.CallerOperatorId.Value,
            source = "OPERATOR_USER_CREATE",
        });
        var activityLog = ActivityLog.Create(
            request.CallerUserId,
            ActivityLogAction.SET_INITIAL_PASSWORD,
            metadata);

        var created = await _subscriptions.TryCreateOperatorUserWithinLimitAsync(
            request.CallerOperatorId.Value,
            user,
            token,
            activityLog,
            role,
            cancellationToken);

        if (!created)
            throw new IdentityDomainException(
                "SUBSCRIPTION_LIMIT_EXCEEDED",
                "Subscription limit exceeded for this operator user role.");

        var usageResource = role switch
        {
            UserRole.DRIVER => SubscriptionUsageResource.DRIVERS,
            UserRole.ASSISTANT => SubscriptionUsageResource.ASSISTANTS,
            UserRole.OPERATOR_STAFF => SubscriptionUsageResource.OPERATOR_USERS,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        var updatedSubscription = await _subscriptions.GetCurrentWithPlanByOperatorIdAsync(
            request.CallerOperatorId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException("Updated operator subscription could not be loaded.");
        await _usageWarnings.EnqueueIfThresholdCrossedAsync(
            updatedSubscription.Subscription,
            updatedSubscription.Plan,
            usageResource,
            1,
            null,
            cancellationToken);

        var setInitialPasswordUrl = _initialPasswordTokens.BuildSetInitialPasswordUrl(code, user.Role);
        await _emailService.SendAccountCreatedLinkAsync(
            user.Email,
            new AccountCreatedEmailDto(user.Id, user.DisplayName, setInitialPasswordUrl, expiresAt),
            cancellationToken);

        return new CreateOperatorUserResponseDto(
            user.Id,
            user.Email,
            phone.Value,
            user.DisplayName,
            user.Role.ToString(),
            user.Status.ToString(),
            user.OperatorId!.Value,
            expiresAt);
    }

    private static PhoneNumber NormalizePhone(string value)
    {
        try
        {
            return PhoneNumber.Normalize(value);
        }
        catch (ArgumentException)
        {
            throw new ValidationException(
                "Invalid phone number format.",
                [new ValidationError("Phone", "Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.")]);
        }
    }
}
