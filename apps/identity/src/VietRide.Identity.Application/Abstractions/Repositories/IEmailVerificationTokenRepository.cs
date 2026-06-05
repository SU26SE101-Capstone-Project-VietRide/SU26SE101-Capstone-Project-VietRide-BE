using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

/// <summary>
/// Repository for the <see cref="EmailVerificationToken"/> aggregate.
/// </summary>
public interface IEmailVerificationTokenRepository : IRepository<EmailVerificationToken, Guid>
{
    /// <summary>
    /// Returns the active token for a specific user, code, and purpose where:
    /// <c>user_id = @userId AND code = @code AND purpose = @purpose
    /// AND used_at IS NULL AND expires_at &gt; @now AND failed_attempts &lt; 5</c>.
    ///
    /// Lookup is scoped to a specific user to prevent cross-user code matching
    /// (Q1 decision: verify uses userId+code+purpose, NOT code+purpose alone).
    /// Retained for future single-shot lookups; verify-email now uses
    /// <see cref="FindByCodeAsync"/> for explicit expired/invalid branching.
    /// </summary>
    Task<EmailVerificationToken?> FindActiveAsync(
        Guid userId,
        string code,
        EmailVerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the token matched ONLY by <c>user_id = @userId AND code = @code
    /// AND purpose = @purpose AND used_at IS NULL</c> — NO expiry/failed_attempts filter.
    /// Allows the verify-email handler to read <see cref="EmailVerificationToken.ExpiresAt"/>
    /// and <see cref="EmailVerificationToken.FailedAttempts"/> and branch explicitly
    /// (expired → AUTH_OTP_EXPIRED; else → increment + AUTH_OTP_INVALID).
    /// Stays user-scoped to prevent cross-user code matching (v7 seam-fix companion).
    /// </summary>
    Task<EmailVerificationToken?> FindByCodeAsync(
        Guid userId,
        string code,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the token matched ONLY by <c>code = @code AND purpose = @purpose
    /// AND used_at IS NULL</c> — NO user scope, expiry, or failed-attempts filter.
    /// Used by the anonymous set-initial-password flow, where the token itself
    /// resolves the target user.
    /// </summary>
    Task<EmailVerificationToken?> FindByCodeAndPurposeAsync(
        string code,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most-recent token for a specific user and purpose where
    /// <c>user_id = @userId AND purpose = @purpose AND used_at IS NULL</c>,
    /// ordered by <c>created_at DESC</c> (newest first). No code match.
    /// Used by the verify-email handler to increment <c>failed_attempts</c>
    /// on a wrong-code attempt when <see cref="FindByCodeAsync"/> returns null
    /// (v7.2 option (b) resolution).
    /// </summary>
    Task<EmailVerificationToken?> FindLatestPendingAsync(
        Guid userId,
        EmailVerificationPurpose purpose,
        CancellationToken ct = default);

    /// <summary>
    /// Attempts to persist <paramref name="entity"/>.
    /// Returns <c>true</c> when the insert succeeds; <c>false</c> when a
    /// unique-constraint violation occurs (e.g. OTP code collision on the
    /// <c>(user_id, code, purpose)</c> index). All other DB errors propagate.
    /// Keeps the EF Core / Npgsql dependency inside Infrastructure where it
    /// belongs, so callers (Application handlers) stay free of
    /// <c>Microsoft.EntityFrameworkCore</c>.
    /// </summary>
    Task<bool> TryAddAsync(EmailVerificationToken entity, CancellationToken ct = default);
}
