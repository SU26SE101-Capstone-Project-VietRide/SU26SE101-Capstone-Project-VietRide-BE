namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Persists failed-login tracking independently of the ambient login transaction so
/// the update survives when invalid credentials return an exception/401.
/// </summary>
public interface IFailedLoginPersister
{
    /// <summary>
    /// Locks and reloads the User, increments the Redis window under that row lock,
    /// applies the fresh counter to the aggregate, and commits immediately.
    /// No-ops when the User no longer exists or is no longer password-login eligible.
    /// </summary>
    Task PersistAsync(
        Guid userId,
        CancellationToken ct = default,
        string clientKind = "UNKNOWN");
}
