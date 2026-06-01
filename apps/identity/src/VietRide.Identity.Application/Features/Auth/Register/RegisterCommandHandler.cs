using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Auth.Register;

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResponseDto>
{
    private const int OtpTtlMinutes = 5;
    private const int OtpMaxRetries = 3;

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailService _email;
    private readonly IOtpRateLimiter _rateLimiter;
    private readonly IClock _clock;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordHasher hasher,
        IEmailService email,
        IOtpRateLimiter rateLimiter,
        IClock clock,
        ILogger<RegisterCommandHandler> logger)
    {
        _users = users;
        _tokens = tokens;
        _hasher = hasher;
        _email = email;
        _rateLimiter = rateLimiter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RegisterResponseDto> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Normalize and validate phone.
        PhoneNumber phone;
        try
        {
            phone = PhoneNumber.Normalize(request.Phone);
        }
        catch (ArgumentException)
        {
            // Bad format is a 400, not a 409. Only a duplicate phone is 409 (BSOT §5.9 line 1263).
            throw new BadRequestException("AUTH_PHONE_INVALID_FORMAT", "Invalid phone number format.");
        }

        // 2. Duplicate email check.
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var existing = await _users.GetByEmailAsync(emailLower, cancellationToken);
        if (existing is not null)
            throw new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email is already registered.");

        // 3. Duplicate phone check.
        var existingByPhone = await _users.GetByPhoneAsync(phone.Value, cancellationToken);
        if (existingByPhone is not null)
            throw new ConflictException("AUTH_PHONE_ALREADY_REGISTERED", "Phone number is already registered.");

        // 4. OTP rate-limit check (max 3/h per email, BSOT §6.9).
        var allowed = await _rateLimiter.TryIncrementAsync(emailLower, cancellationToken);
        if (!allowed)
            throw new TooManyRequestsException("AUTH_OTP_RATE_LIMIT_EXCEEDED", "Too many OTP requests. Please try again later.");

        // 5. Hash password.
        var passwordHash = _hasher.Hash(request.Password);

        // 6. Create user entity.
        var user = User.CreatePassenger(
            email: emailLower,
            phone: phone,
            passwordHash: passwordHash,
            displayName: request.DisplayName.Trim());

        await _users.AddAsync(user, cancellationToken);

        // 7. Generate OTP with retry on collision.
        var otpToken = await CreateOtpWithRetryAsync(user.Id, cancellationToken);

        // 8. Send OTP email (LoggingEmailService in Day 3 — logs to Serilog).
        await _email.SendOtpAsync(
            to: user.Email,
            code: otpToken.Code,
            purpose: EmailOtpPurpose.REGISTRATION,
            ttlMinutes: OtpTtlMinutes,
            ct: cancellationToken);

        return new RegisterResponseDto(
            UserId: user.Id,
            Email: user.Email,
            Status: user.Status.ToString(),
            OtpTtlMinutes: OtpTtlMinutes);
    }

    private async Task<EmailVerificationToken> CreateOtpWithRetryAsync(
        Guid userId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < OtpMaxRetries; attempt++)
        {
            var code = GenerateOtpCode();
            var token = EmailVerificationToken.Create(
                userId: userId,
                purpose: EmailVerificationPurpose.REGISTRATION,
                code: code,
                expiresAt: _clock.UtcNow.AddMinutes(OtpTtlMinutes));

            var inserted = await _tokens.TryAddAsync(token, ct);
            if (inserted)
                return token;

            _logger.LogWarning(
                "OTP code collision on attempt {Attempt} for userId={UserId}; retrying.",
                attempt + 1,
                userId);
        }

        _logger.LogError("OTP code collision exceeded {MaxRetries} retries for userId={UserId}.", OtpMaxRetries, userId);
        throw new InvalidOperationException("Failed to generate a unique OTP code after multiple attempts.");
    }

    /// <summary>
    /// Generates a cryptographically random 6-digit numeric OTP.
    /// Uses <see cref="System.Security.Cryptography.RandomNumberGenerator"/> — NEVER System.Random.
    /// </summary>
    private static string GenerateOtpCode()
        => System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");
}
