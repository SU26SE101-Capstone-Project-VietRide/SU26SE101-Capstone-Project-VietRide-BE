using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.ResetPassword;

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ResetPasswordResponseDto>
{
    private const int MaxFailedAttempts = 5;

    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IOtpFailedAttemptPersister _failedAttemptPersister;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    public ResetPasswordCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IRefreshTokenRepository refreshTokens,
        IOtpFailedAttemptPersister failedAttemptPersister,
        IPasswordHasher passwordHasher,
        IClock clock)
    {
        _users = users;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _failedAttemptPersister = failedAttemptPersister;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task<ResetPasswordResponseDto> Handle(
        ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken)
            ?? throw InvalidOtp();

        if (user.Status != UserStatus.ACTIVE)
            throw InvalidOtp();

        var token = await _tokens.FindByCodeAsync(
            user.Id,
            request.Code,
            EmailVerificationPurpose.PASSWORD_RESET,
            cancellationToken);

        if (token is null)
        {
            await _failedAttemptPersister.PersistAsync(
                user.Id,
                EmailVerificationPurpose.PASSWORD_RESET,
                cancellationToken);

            throw InvalidOtp();
        }

        var now = _clock.UtcNow;
        if (token.ExpiresAt <= now)
            throw new BadRequestException("AUTH_OTP_EXPIRED", "Verification code has expired.");

        if (token.FailedAttempts >= MaxFailedAttempts)
            throw InvalidOtp();

        user.ResetPassword(_passwordHasher.Hash(request.NewPassword));
        token.MarkUsed(now);
        _tokens.Update(token);
        await _refreshTokens.RevokeActiveByUserAsync(
            user.Id,
            RefreshTokenRevokeReason.PASSWORD_RESET,
            cancellationToken);

        return new ResetPasswordResponseDto(user.Id, user.Status.ToString());
    }

    private static BadRequestException InvalidOtp()
        => new("AUTH_OTP_INVALID", "Invalid verification code.");
}
