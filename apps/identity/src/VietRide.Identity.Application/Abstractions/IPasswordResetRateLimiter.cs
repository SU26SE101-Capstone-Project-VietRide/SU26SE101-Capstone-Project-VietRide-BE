namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Enforces password-reset OTP send limits independently from registration OTPs.
/// Key pattern: <c>identity:pwd_reset_rate:{email}</c>, max 3 sends per hour.
/// </summary>
public interface IPasswordResetRateLimiter
{
    Task<bool> TryIncrementAsync(string email, CancellationToken ct = default);
}
