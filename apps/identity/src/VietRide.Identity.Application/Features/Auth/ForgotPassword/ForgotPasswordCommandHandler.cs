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

namespace VietRide.Identity.Application.Features.Auth.ForgotPassword;

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResponseDto>
{
    private const int OtpTtlMinutes = 5;
    private const int OtpMaxRetries = 3;

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IPasswordResetRateLimiter _rateLimiter;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordResetRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _users = users;
        _tokens = tokens;
        _rateLimiter = rateLimiter;
        _clock = clock;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<ForgotPasswordResponseDto> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var allowed = await _rateLimiter.TryIncrementAsync(emailLower, cancellationToken);
        if (!allowed)
            throw new TooManyRequestsException("AUTH_OTP_RATE_LIMIT_EXCEEDED", "Too many OTP requests. Please try again later.");

        var user = await _users.GetByEmailAsync(emailLower, cancellationToken);
        if (user is null || user.Status != UserStatus.ACTIVE)
        {
            _logger.LogInformation(
                "Password reset requested for non-eligible email {Email}; returning generic success.",
                emailLower);
            return new ForgotPasswordResponseDto(emailLower, OtpTtlMinutes);
        }

        var now = _clock.UtcNow;
        await _tokens.RevokeActiveByUserAndPurposeAsync(
            user.Id,
            EmailVerificationPurpose.PASSWORD_RESET,
            now,
            cancellationToken);

        var otpToken = await CreateOtpWithRetryAsync(user.Id, cancellationToken);
        var otpEvent = new OtpRequestedIntegrationEvent(
            user.Id,
            user.Email,
            otpToken.Code,
            EmailOtpPurpose.PASSWORD_RESET.ToString(),
            OtpTtlMinutes);

        await _outbox.EnqueueAsync(
            OtpRequestedIntegrationEvent.EventType,
            JsonSerializer.Serialize(otpEvent),
            cancellationToken);

        return new ForgotPasswordResponseDto(user.Email, OtpTtlMinutes);
    }

    private async Task<EmailVerificationToken> CreateOtpWithRetryAsync(
        Guid userId,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < OtpMaxRetries; attempt++)
        {
            var token = EmailVerificationToken.Create(
                userId,
                EmailVerificationPurpose.PASSWORD_RESET,
                GenerateOtpCode(),
                _clock.UtcNow.AddMinutes(OtpTtlMinutes));

            var inserted = await _tokens.TryAddAsync(token, ct);
            if (inserted)
                return token;

            _logger.LogWarning(
                "Password reset OTP code collision on attempt {Attempt} for userId={UserId}; retrying.",
                attempt + 1,
                userId);
        }

        _logger.LogError("Password reset OTP collision exceeded {MaxRetries} retries for userId={UserId}.", OtpMaxRetries, userId);
        throw new InvalidOperationException("Failed to generate a unique password reset OTP code after multiple attempts.");
    }

    private static string GenerateOtpCode()
        => System.Security.Cryptography.RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");
}
