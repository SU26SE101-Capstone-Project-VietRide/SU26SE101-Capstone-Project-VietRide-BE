namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Persists failed-login tracking independently of the ambient login transaction so
/// the update survives when invalid credentials return an exception/401.
/// </summary>
public interface IFailedLoginPersister
{
    /// <summary>
    /// Applies the windowed failed-login counter to the user aggregate and commits
    /// immediately. No-ops silently if the user no longer exists.
    /// </summary>
    Task PersistAsync(Guid userId, long failedAttemptsInWindow, CancellationToken ct = default);
}
