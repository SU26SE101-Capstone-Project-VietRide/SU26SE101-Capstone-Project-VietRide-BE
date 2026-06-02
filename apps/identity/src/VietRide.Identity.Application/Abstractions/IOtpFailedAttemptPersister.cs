using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Persists an OTP <c>failed_attempts</c> increment for the newest pending token
/// owned by <paramref name="userId"/> for the given <paramref name="purpose"/>.
/// The implementation MUST commit on its own independent unit of work so the
/// increment survives even when the ambient request transaction is rolled back
/// (e.g. the verify-email handler throws <c>BadRequestException</c> after a wrong
/// code, causing <c>TransactionBehavior</c> to roll back the outer transaction).
/// </summary>
public interface IOtpFailedAttemptPersister
{
    /// <summary>
    /// Finds the newest pending token for <paramref name="userId"/> +
    /// <paramref name="purpose"/>, calls
    /// <see cref="Domain.Entities.EmailVerificationToken.IncrementFailedAttempts"/>,
    /// and flushes via its own <c>SaveChangesAsync</c> call before returning.
    /// No-ops silently when no matching pending token exists.
    /// </summary>
    Task PersistAsync(Guid userId, EmailVerificationPurpose purpose, CancellationToken ct = default);
}
