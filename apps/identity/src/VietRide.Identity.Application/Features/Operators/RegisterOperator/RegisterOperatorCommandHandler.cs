using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Operators.RegisterOperator;

public sealed class RegisterOperatorCommandHandler : IRequestHandler<RegisterOperatorCommand, RegisterOperatorResponseDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int OtpExpiresInMinutes = 10;
    private const int OtpMaxRetries = 3;

    private readonly IOperatorRepository _operators;
    private readonly IUserRepository _users;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly ISubscriptionPlanRepository _subscriptionPlans;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<RegisterOperatorCommandHandler> _logger;

    public RegisterOperatorCommandHandler(
        IOperatorRepository operators,
        IUserRepository users,
        IOperatorSubscriptionRepository operatorSubscriptions,
        ISubscriptionPlanRepository subscriptionPlans,
        IEmailVerificationTokenRepository tokens,
        IActivityLogRepository activityLogs,
        IPasswordHasher passwordHasher,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<RegisterOperatorCommandHandler> logger)
    {
        _operators = operators;
        _users = users;
        _operatorSubscriptions = operatorSubscriptions;
        _subscriptionPlans = subscriptionPlans;
        _tokens = tokens;
        _activityLogs = activityLogs;
        _passwordHasher = passwordHasher;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RegisterOperatorResponseDto> Handle(
        RegisterOperatorCommand request,
        CancellationToken cancellationToken)
    {
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

        var passwordHash = _passwordHasher.Hash(request.Password);
        var adminUser = User.CreateOperatorAdminPendingEmailVerification(
            email,
            representativePhone,
            passwordHash,
            request.RepresentativeName.Trim(),
            operatorEntity.Id);

        var subscription = OperatorSubscription.CreatePendingApproval(operatorEntity.Id, starterPlan.Id, now);
        subscription.IncrementUsage(SubscriptionUsageResource.OPERATOR_USERS, 1);

        await _operators.AddAsync(operatorEntity, cancellationToken);
        await _users.AddAsync(adminUser, cancellationToken);
        await _operatorSubscriptions.AddAsync(subscription, cancellationToken);

        var token = await CreateOtpWithRetryAsync(adminUser.Id, cancellationToken);

        // Enqueue OTP email event into the Outbox (same EF transaction via TransactionBehavior).
        // Notification Service consumes and delivers the email asynchronously.
        var otpEvent = new OtpRequestedIntegrationEvent(
            adminUser.Id,
            adminUser.Email,
            token.Code,
            EmailOtpPurpose.REGISTRATION.ToString(),
            OtpExpiresInMinutes);
        await _outbox.EnqueueAsync(
            OtpRequestedIntegrationEvent.EventType,
            JsonSerializer.Serialize(otpEvent),
            cancellationToken);

        var registrationEvent = new OperatorRegistrationSubmittedIntegrationEvent(
            Guid.NewGuid(),
            now,
            operatorEntity.Id,
            operatorEntity.Name);
        await _outbox.EnqueueAsync(
            registrationEvent.EventId,
            registrationEvent.EventType,
            JsonSerializer.Serialize(registrationEvent, JsonOptions),
            cancellationToken);

        _logger.LogDebug(
            "Operator registration OTP for {Email}: {Code} (purpose REGISTRATION, ttl {TtlMinutes}m).",
            adminUser.Email,
            token.Code,
            OtpExpiresInMinutes);

        var metadata = JsonSerializer.Serialize(new
        {
            operatorId = operatorEntity.Id,
            actorUserId = adminUser.Id,
            source = "SELF_REGISTER",
        });

        await _activityLogs.AddAsync(
            ActivityLog.Create(adminUser.Id, ActivityLogAction.CREATE_OPERATOR, metadata),
            cancellationToken);

        return new RegisterOperatorResponseDto(
            operatorEntity.Id,
            "Đơn đăng ký đã nhận, vui lòng xác thực email");
    }

    private async Task<EmailVerificationToken> CreateOtpWithRetryAsync(
        Guid userId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < OtpMaxRetries; attempt++)
        {
            var code = GenerateOtpCode();
            var token = EmailVerificationToken.Create(
                userId,
                EmailVerificationPurpose.REGISTRATION,
                code,
                _clock.UtcNow.AddMinutes(OtpExpiresInMinutes));

            var inserted = await _tokens.TryAddAsync(token, ct);
            if (inserted)
                return token;

            _logger.LogWarning(
                "OTP code collision on attempt {Attempt} for operator admin userId={UserId}; retrying.",
                attempt + 1,
                userId);
        }

        _logger.LogError("OTP code collision exceeded {MaxRetries} retries for operator admin userId={UserId}.", OtpMaxRetries, userId);
        throw new InvalidOperationException("Failed to generate a unique OTP code after multiple attempts.");
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

    private static string GenerateOtpCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }
}
