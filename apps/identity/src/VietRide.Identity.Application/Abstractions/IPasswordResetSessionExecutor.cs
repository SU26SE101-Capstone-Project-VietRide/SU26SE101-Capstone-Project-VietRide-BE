namespace VietRide.Identity.Application.Abstractions;

public enum PasswordResetSessionStatus
{
    SUCCEEDED,
    INVALID_OTP,
    EXPIRED_OTP,
}

public sealed record PasswordResetSessionResult(
    PasswordResetSessionStatus Status,
    Guid? UserId = null,
    string? UserStatus = null);

/// <summary>
/// Executes password-reset OTP validation/failure persistence and password/token mutation in a
/// fresh transaction ordered User -> EmailVerificationToken -> RefreshToken.
/// </summary>
public interface IPasswordResetSessionExecutor
{
    Task<PasswordResetSessionResult> ExecuteAsync(
        Guid userId,
        string code,
        string passwordHash,
        CancellationToken ct = default);
}
