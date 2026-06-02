using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

/// <summary>
/// Repository for the <see cref="User"/> aggregate.
/// </summary>
public interface IUserRepository : IRepository<User, Guid>
{
    /// <summary>
    /// Returns the non-deleted user whose email matches <paramref name="emailLower"/>
    /// (case-insensitive comparison via the partial unique index on LOWER(email)).
    /// </summary>
    Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default);

    /// <summary>
    /// Returns the non-deleted user whose phone matches the E.164 value
    /// <paramref name="e164Phone"/>.
    /// </summary>
    Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default);
}
