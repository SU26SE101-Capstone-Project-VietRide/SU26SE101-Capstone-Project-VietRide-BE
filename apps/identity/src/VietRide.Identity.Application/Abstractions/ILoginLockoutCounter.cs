namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Tracks failed login attempts in the BSOT-mandated 15-minute Redis window.
/// Key pattern: <c>identity:login_lockout:{userId}</c>.
/// </summary>
public interface ILoginLockoutCounter
{
    /// <summary>
    /// Increments the failed-login counter for <paramref name="userId"/> and returns
    /// the counter value after increment. The implementation starts a 15-minute TTL
    /// when the first failure enters the window.
    /// </summary>
    Task<long> IncrementAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Clears the active failed-login window after a successful login.
    /// </summary>
    Task ResetAsync(Guid userId, CancellationToken ct = default);
}
