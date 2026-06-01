namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Enforces OTP send rate limits to prevent abuse.
/// Per BSOT §6.9: max 3 OTPs per hour per email (Redis key <c>identity:otp_rate:{email}</c>,
/// TTL 1 hour, sliding counter via INCR + EXPIRE).
/// </summary>
public interface IOtpRateLimiter
{
    /// <summary>
    /// Increments the OTP send counter for the given email.
    /// Returns <c>true</c> if the send is allowed (count ≤ 3 after increment);
    /// returns <c>false</c> if the limit has been exceeded.
    /// </summary>
    Task<bool> TryIncrementAsync(string email, CancellationToken ct = default);
}
