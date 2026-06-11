using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed class CreateOperatorCommandHandler : IRequestHandler<CreateOperatorCommand, CreateOperatorResponseDto>
{
    private const string SetInitialPasswordUrlBase = "https://app.vietride.app/auth/set-password?token=";

    private readonly IOperatorRepository _operators;
    private readonly IUserRepository _users;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly ISubscriptionPlanRepository _subscriptionPlans;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IInitialPasswordTokenService _initialPasswordTokens;
    private readonly IEmailService _emailService;
    private readonly IClock _clock;

    public CreateOperatorCommandHandler(
        IOperatorRepository operators,
        IUserRepository users,
        IOperatorSubscriptionRepository operatorSubscriptions,
        ISubscriptionPlanRepository subscriptionPlans,
        IEmailVerificationTokenRepository tokens,
        IActivityLogRepository activityLogs,
        IInitialPasswordTokenService initialPasswordTokens,
        IEmailService emailService,
        IClock clock)
    {
        _operators = operators;
        _users = users;
        _operatorSubscriptions = operatorSubscriptions;
        _subscriptionPlans = subscriptionPlans;
        _tokens = tokens;
        _activityLogs = activityLogs;
        _initialPasswordTokens = initialPasswordTokens;
        _emailService = emailService;
        _clock = clock;
    }

    public async Task<CreateOperatorResponseDto> Handle(
        CreateOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can create operators.");

        if (request.UnsupportedSubscriptionFields.Count > 0)
        {
            throw new ValidationException(
                "Day-6 manual operator creation only supports the Starter Free Trial default plan.",
                request.UnsupportedSubscriptionFields
                    .Select(field => new ValidationError(field, "Paid plans and explicit subscription fields are not supported by this endpoint yet."))
                    .ToArray());
        }

        var businessRegistrationNumber = request.BusinessRegistrationNumber.Trim();
        if (await _operators.GetByBusinessRegistrationNumberAsync(businessRegistrationNumber, cancellationToken) is not null)
            throw new ConflictException("OPERATOR_DUPLICATE_REGISTRATION", "Business registration number is already registered.");

        var taxCode = request.TaxCode.Trim();
        if (await _operators.GetByTaxCodeAsync(taxCode, cancellationToken) is not null)
            throw new ConflictException("OPERATOR_DUPLICATE_TAX_CODE", "Tax code is already registered.");

        var email = request.ContactEmail.Trim().ToLowerInvariant();
        if (await _users.GetByEmailAsync(email, cancellationToken) is not null)
            throw new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email is already registered.");

        var contactPhone = NormalizePhone(request.ContactPhone, nameof(request.ContactPhone));
        var representativePhone = NormalizePhone(request.RepresentativePhone, nameof(request.RepresentativePhone));
        if (await _users.GetByPhoneAsync(representativePhone.ToString(), cancellationToken) is not null)
            throw new ConflictException("AUTH_PHONE_ALREADY_REGISTERED", "Phone is already registered.");

        var starterPlan = await _subscriptionPlans.GetStarterPlanAsync(cancellationToken)
            ?? throw new ValidationException(
                "Starter subscription plan is not configured.",
                [new ValidationError("plan", "Starter subscription plan is not configured.")]);

        var now = _clock.UtcNow;
        var operatorEntity = Operator.CreatePending(
            request.Name.Trim(),
            businessRegistrationNumber,
            taxCode,
            email,
            contactPhone.ToString(),
            request.AddressStreet.Trim(),
            request.AddressWard.Trim(),
            request.AddressDistrict.Trim(),
            request.AddressProvince.Trim(),
            request.RepresentativeName.Trim(),
            representativePhone.ToString());
        operatorEntity.Approve(request.CallerUserId, now);

        var adminUser = User.CreateOperatorAdminPendingPassword(
            email,
            representativePhone,
            request.RepresentativeName.Trim(),
            operatorEntity.Id);

        var expiresAt = now.AddDays(30);
        var subscription = OperatorSubscription.CreateActiveTrial(operatorEntity.Id, starterPlan.Id, now, expiresAt);
        subscription.IncrementUsage(SubscriptionUsageResource.OPERATOR_USERS, 1);

        await _operators.AddAsync(operatorEntity, cancellationToken);
        await _users.AddAsync(adminUser, cancellationToken);
        await _operatorSubscriptions.AddAsync(subscription, cancellationToken);

        var code = _initialPasswordTokens.GenerateCode();
        var tokenExpiresAt = _initialPasswordTokens.GetExpiresAt(now);
        var token = EmailVerificationToken.Create(
            adminUser.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            code,
            tokenExpiresAt);
        await _tokens.AddAsync(token, cancellationToken);

        var setInitialPasswordUrl = SetInitialPasswordUrlBase + code;
        await _emailService.SendAccountCreatedLinkAsync(
            adminUser.Email,
            new AccountCreatedEmailDto(adminUser.Id, adminUser.DisplayName, setInitialPasswordUrl, tokenExpiresAt),
            cancellationToken);

        var metadata = JsonSerializer.Serialize(new
        {
            operatorId = operatorEntity.Id,
            actorUserId = request.CallerUserId,
            targetUserId = adminUser.Id,
            source = "SYSTEM_ADMIN_CREATE_OPERATOR",
        });

        await _activityLogs.AddAsync(
            ActivityLog.Create(request.CallerUserId, ActivityLogAction.CREATE_OPERATOR, metadata),
            cancellationToken);

        return new CreateOperatorResponseDto(
            new OperatorSummaryDto(
                operatorEntity.Id,
                operatorEntity.Name,
                operatorEntity.RegistrationStatus.ToString(),
                operatorEntity.ContactEmail,
                operatorEntity.ContactPhone,
                operatorEntity.BusinessRegistrationNumber,
                operatorEntity.TaxCode),
            new OperatorAdminSummaryDto(
                adminUser.Id,
                adminUser.Email,
                adminUser.Phone?.ToString() ?? string.Empty,
                adminUser.DisplayName,
                adminUser.Role.ToString(),
                adminUser.Status.ToString()),
            new OperatorSubscriptionSummaryDto(
                subscription.Id,
                subscription.PlanId,
                starterPlan.Name,
                subscription.Status.ToString(),
                subscription.StartedAt,
                subscription.ExpiresAt,
                subscription.CurrentOperatorUsers));
    }

    private static PhoneNumber NormalizePhone(string value, string fieldName)
    {
        try
        {
            return PhoneNumber.Normalize(value);
        }
        catch (ArgumentException)
        {
            throw new ValidationException(
                "Invalid phone number format.",
                [new ValidationError(fieldName, "Phone number must be a Vietnamese number in +84xxxxxxxxx or 0xxxxxxxxx format.")]);
        }
    }
}
