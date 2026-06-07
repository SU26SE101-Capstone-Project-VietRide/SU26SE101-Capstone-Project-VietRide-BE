using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Domain.Entities;

public sealed class User : BaseEntity<Guid>, ISoftDeletable
{
    private const int MaxFailedLoginAttempts = 5;

    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// E.164 VN format e.g. +84901234567. REQUIRED for PASSENGER self-registration.
    /// Stored via PhoneNumber value object; EF maps the string value.
    /// </summary>
    public PhoneNumber? Phone { get; private set; }

    /// <summary>BCrypt cost 12. Nullable for Google-only accounts.</summary>
    public string? PasswordHash { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public Guid? OperatorId { get; private set; }

    // Account lockout tracking — no LockedUntil column in schema.
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LastFailedLoginAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    // Soft-delete: users table has no is_active column; deleted_at IS NULL is the live-row filter.
    public DateTimeOffset? DeletedAt { get; private set; }

    private User() { }

    /// <summary>
    /// Factory for PASSENGER self-registration via email + password.
    /// Phone MUST already be normalized to E.164 by the Application layer.
    /// </summary>
    public static User CreatePassenger(
        string email,
        PhoneNumber phone,
        string passwordHash,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = UserRole.PASSENGER,
            Status = UserStatus.PENDING_EMAIL_VERIFICATION,
            FailedLoginAttempts = 0,
        };
    }

    /// <summary>
    /// Factory for Google OAuth PASSENGER accounts. Google has already verified the email.
    /// Phone is completed later through the complete-profile flow.
    /// </summary>
    public static User CreateGoogleAccount(
        string email,
        string displayName,
        string? avatarUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = null,
            PasswordHash = null,
            DisplayName = displayName,
            AvatarUrl = avatarUrl,
            Role = UserRole.PASSENGER,
            Status = UserStatus.ACTIVE,
            FailedLoginAttempts = 0,
        };
    }

    /// <summary>
    /// Factory for SYSTEM_ADMIN users that must set their initial password later.
    /// </summary>
    public static User CreateAdminPendingPassword(
        string email,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = null,
            PasswordHash = null,
            DisplayName = displayName,
            Role = UserRole.SYSTEM_ADMIN,
            Status = UserStatus.PENDING_INITIAL_PASSWORD,
            OperatorId = null,
            FailedLoginAttempts = 0,
        };
    }

    /// <summary>
    /// Factory for the initial OPERATOR_ADMIN created by public operator self-registration.
    /// The account owns a password immediately but must verify email before approval/login.
    /// </summary>
    public static User CreateOperatorAdminPendingEmailVerification(
        string email,
        PhoneNumber phone,
        string passwordHash,
        string displayName,
        Guid operatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfEqual(operatorId, Guid.Empty);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = UserRole.OPERATOR_ADMIN,
            Status = UserStatus.PENDING_EMAIL_VERIFICATION,
            OperatorId = operatorId,
            FailedLoginAttempts = 0,
        };
    }

    /// <summary>
    /// Factory for an OPERATOR_ADMIN manually created by SYSTEM_ADMIN.
    /// The account must set its first password through the emailed initial-password link.
    /// </summary>
    public static User CreateOperatorAdminPendingPassword(
        string email,
        PhoneNumber phone,
        string displayName,
        Guid operatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfEqual(operatorId, Guid.Empty);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone,
            PasswordHash = null,
            DisplayName = displayName,
            Role = UserRole.OPERATOR_ADMIN,
            Status = UserStatus.PENDING_INITIAL_PASSWORD,
            OperatorId = operatorId,
            FailedLoginAttempts = 0,
        };
    }

    // ---------------------------------------------------------------------------
    // Domain methods — status transitions + lockout tracking
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Transitions status from PENDING_EMAIL_VERIFICATION to ACTIVE.
    /// </summary>
    public void VerifyEmail()
    {
        if (Status != UserStatus.PENDING_EMAIL_VERIFICATION)
        {
            throw new InvalidUserStatusTransitionException(
                Status.ToString(),
                UserStatus.ACTIVE.ToString());
        }

        Status = UserStatus.ACTIVE;
    }

    /// <summary>
    /// Sets the first password for admin/operator accounts created in PENDING_INITIAL_PASSWORD.
    /// </summary>
    public void SetInitialPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (Status != UserStatus.PENDING_INITIAL_PASSWORD)
        {
            throw new InvalidUserStatusTransitionException(
                Status.ToString(),
                UserStatus.ACTIVE.ToString());
        }

        PasswordHash = passwordHash;
        Status = UserStatus.ACTIVE;
    }

    /// <summary>
    /// Completes a Google-created profile by setting the already-normalized phone once.
    /// Existing phone changes must use the dedicated profile-update flow.
    /// </summary>
    public void CompleteProfile(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        if (Phone is not null)
        {
            throw new IdentityDomainException(
                "VALIDATION_ERROR",
                "Phone is already set and cannot be overwritten through complete profile.");
        }

        Phone = phone;
    }

    /// <summary>
    /// Records a failed login and sets last_failed_login_at.
    /// <paramref name="failedAttemptsInWindow"/> is the Redis counter value for the
    /// 15-minute <c>identity:login_lockout:{userId}</c> window. When that windowed
    /// counter reaches >= 5 the status transitions permanently to LOCKED.
    /// The lock is permanent — only a System Admin unlocks the account manually.
    /// </summary>
    public void RecordFailedLogin(IClock clock, long failedAttemptsInWindow)
    {
        if (failedAttemptsInWindow < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedAttemptsInWindow),
                failedAttemptsInWindow,
                "Failed login attempts in window must be at least 1.");
        }

        FailedLoginAttempts = failedAttemptsInWindow > int.MaxValue
            ? int.MaxValue
            : (int)failedAttemptsInWindow;
        LastFailedLoginAt = clock.UtcNow;

        if (failedAttemptsInWindow >= MaxFailedLoginAttempts)
        {
            Status = UserStatus.LOCKED;
        }
    }

    /// <summary>
    /// Zeros the failed-login counter. Exposed for explicit admin/test use.
    /// Note: <see cref="RecordSuccessfulLogin"/> already resets the same fields
    /// internally — the login handler only needs to call <see cref="RecordSuccessfulLogin"/>;
    /// calling this beforehand is a no-op.
    /// </summary>
    public void ResetFailedLogins()
    {
        FailedLoginAttempts = 0;
        LastFailedLoginAt = null;
    }

    /// <summary>
    /// Records the timestamp of a successful login and resets the failed counter.
    /// Called by the login handler on success.
    /// </summary>
    public void RecordSuccessfulLogin(IClock clock)
    {
        LastLoginAt = clock.UtcNow;
        FailedLoginAttempts = 0;
        LastFailedLoginAt = null;
    }

    /// <summary>
    /// Locks the account manually (e.g. Admin action). Status becomes LOCKED.
    /// </summary>
    public void Lock()
    {
        Status = UserStatus.LOCKED;
    }

    /// <summary>
    /// Soft-deletes the user account. Sets <see cref="DeletedAt"/> and transitions
    /// <see cref="Status"/> to <see cref="UserStatus.DELETED"/>.
    /// Task 3.2 EF config adds a global query filter on <c>deleted_at IS NULL</c>.
    /// </summary>
    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        Status = UserStatus.DELETED;
    }
}
