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

namespace VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;

public sealed class ResendVerificationEmailCommandHandler
    : IRequestHandler<ResendVerificationEmailCommand, ResendVerificationEmailResponseDto>
{
    private const int OtpTtlMinutes = 5;
    private const int OtpMaxRetries = 3;

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IOtpRateLimiter _rateLimiter;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ILogger<ResendVerificationEmailCommandHandler> _logger;

    public ResendVerificationEmailCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IOtpRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox,
        ILogger<ResendVerificationEmailCommandHandler> logger)
    {
        _users = users;
        _tokens = tokens;
        _rateLimiter = rateLimiter;
        _clock = clock;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<ResendVerificationEmailResponseDto> Handle(
        ResendVerificationEmailCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmailVerificationPurpose>(request.Purpose, ignoreCase: true, out var purpose)
            || purpose != EmailVerificationPurpose.REGISTRATION)
        {
            throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");
        }

        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken)
            ?? throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");

        if (user.Status == UserStatus.ACTIVE)
            throw new ConflictException("AUTH_EMAIL_ALREADY_VERIFIED", "Email is already verified.");

        if (user.Status != UserStatus.PENDING_EMAIL_VERIFICATION)
            throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");

        var allowed = await _rateLimiter.TryIncrementAsync(emailLower, cancellationToken);
        if (!allowed)
            throw new TooManyRequestsException("AUTH_OTP_RATE_LIMIT_EXCEEDED", "Too many OTP requests. Please try again later.");

        var now = _clock.UtcNow;
        await _tokens.RevokeActiveByUserAndPurposeAsync(user.Id, purpose, now, cancellationToken);

        var otpToken = await CreateOtpWithRetryAsync(user.Id, purpose, cancellationToken);
        var otpEvent = new OtpRequestedIntegrationEvent(
            user.Id,
            user.Email,
            otpToken.Code,
            EmailOtpPurpose.REGISTRATION.ToString(),
            OtpTtlMinutes);

        await _outbox.EnqueueAsync(
            OtpRequestedIntegrationEvent.EventType,
            JsonSerializer.Serialize(otpEvent),
            cancellationToken);

        _logger.LogDebug(
            "Resent registration OTP for {Email}: {Code} (ttl {TtlMinutes}m).",
            user.Email,
            otpToken.Code,
            OtpTtlMinutes);

        return new ResendVerificationEmailResponseDto(
            user.Email,
            user.Status.ToString(),
            OtpTtlMinutes);
    }

    private async Task<EmailVerificationToken> CreateOtpWithRetryAsync(
        Guid userId,
        EmailVerificationPurpose purpose,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < OtpMaxRetries; attempt++)
        {
            var code = GenerateOtpCode();
            var token = EmailVerificationToken.Create(
                userId,
                purpose,
                code,
                _clock.UtcNow.AddMinutes(OtpTtlMinutes));

            var inserted = await _tokens.TryAddAsync(token, ct);
            if (inserted)
                return token;

            _logger.LogWarning(
                "OTP code collision on resend attempt {Attempt} for userId={UserId}; retrying.",
                attempt + 1,
                userId);
        }

        _logger.LogError("OTP code collision exceeded {MaxRetries} retries for userId={UserId}.", OtpMaxRetries, userId);
        throw new InvalidOperationException("Failed to generate a unique OTP code after multiple attempts.");
    }

    private static string GenerateOtpCode()
        => System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");
}
