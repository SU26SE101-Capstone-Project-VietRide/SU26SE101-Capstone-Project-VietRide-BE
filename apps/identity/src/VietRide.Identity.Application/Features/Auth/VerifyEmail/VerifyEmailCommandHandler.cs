using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.VerifyEmail;

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, VerifyEmailResponseDto>
{
    private const int MaxFailedAttempts = 5;

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IClock _clock;

    public VerifyEmailCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IClock clock)
    {
        _users = users;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<VerifyEmailResponseDto> Handle(
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve purpose enum — use a generic message, do NOT echo user-supplied purpose (SF3).
        if (!Enum.TryParse<EmailVerificationPurpose>(request.Purpose, ignoreCase: true, out var purpose))
            throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");

        // 2. Find the user by email.
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken)
            ?? throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");

        var now = _clock.UtcNow;

        // 3. Exact-match lookup: user_id + code + purpose + used_at IS NULL (no expiry/attempts filter).
        //    This lets the handler inspect ExpiresAt and FailedAttempts to branch explicitly.
        var token = await _tokens.FindByCodeAsync(user.Id, request.Code, purpose, cancellationToken);

        if (token is null)
        {
            // Case (1): no matching row — wrong code. Increment failed_attempts on the newest
            // pending token for this user+purpose (v7.2 option (b): FindLatestPendingAsync).
            var latestPending = await _tokens.FindLatestPendingAsync(user.Id, purpose, cancellationToken);
            if (latestPending is not null)
            {
                latestPending.IncrementFailedAttempts();
                _tokens.Update(latestPending);
            }

            throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");
        }

        // Case (2): row found but expired — do NOT increment (expiry is not a wrong-code attempt).
        if (token.ExpiresAt <= now)
            throw new BadRequestException("AUTH_OTP_EXPIRED", "Verification code has expired.");

        // Case (3): row found, not expired, but already burned (>= 5 failures).
        if (token.FailedAttempts >= MaxFailedAttempts)
            throw new BadRequestException("AUTH_OTP_INVALID", "Invalid verification code.");

        // Case (4): success — mark token used and apply domain transition.
        token.MarkUsed(now);
        _tokens.Update(token);
        user.VerifyEmail();

        return new VerifyEmailResponseDto(
            UserId: user.Id,
            Status: user.Status.ToString());
    }
}
