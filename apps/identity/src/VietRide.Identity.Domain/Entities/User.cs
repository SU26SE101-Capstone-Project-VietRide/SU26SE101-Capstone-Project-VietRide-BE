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
    /// Increments failed login counter and sets last_failed_login_at.
    /// When the counter reaches >= 5 the status transitions permanently to LOCKED.
    /// The lock is permanent — only a System Admin unlocks the account manually.
    /// Note: the 15-minute window is enforced by the Redis TTL on
    /// identity:login_lockout:{userId} (Application layer), NOT by a DB column.
    /// </summary>
    public void RecordFailedLogin(IClock clock)
    {
        FailedLoginAttempts++;
        LastFailedLoginAt = clock.UtcNow;

        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
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
