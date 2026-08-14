using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;

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

    /// <summary>
    /// Acquires a PostgreSQL row lock and returns a freshly reloaded User entity.
    /// Callers must already be inside a transaction.
    /// </summary>
    Task<User?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    /// <summary>
    /// Acquires a row lock for a Driver or Assistant in the exact operator tenant.
    /// Returns null for cross-tenant and non-manageable roles to preserve tenant masking.
    /// </summary>
    Task<User?> GetManageableOperatorUserForUpdateAsync(
        Guid id,
        Guid operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator-scoped user locking is not implemented by this repository.");

    Task<PagedResult<User>> ListAdminUsersAsync(
        QueryOptions options,
        UserRole? role,
        UserStatus? status,
        Guid? operatorId,
        CancellationToken ct = default)
        => throw new NotSupportedException("Admin user listing is not implemented by this repository.");

    Task<PagedResult<User>> ListAdminUsersFilteredAsync(
        QueryOptions options,
        UserRole? role,
        UserStatus? status,
        Guid? operatorId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtcExclusive = null,
        CancellationToken ct = default)
        => ListAdminUsersAsync(options, role, status, operatorId, ct);

    Task<PagedResult<User>> ListOperatorUsersAsync(
        QueryOptions options,
        Guid? operatorId,
        UserRole? role,
        UserStatus? status,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListActiveOperatorAdminIdsAsync(
        Guid operatorId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListOperatorAdminIdsAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => ListActiveOperatorAdminIdsAsync(operatorId, ct);

    Task<IReadOnlyList<Guid>> ListOperatorScopedUserIdsAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => ListOperatorAdminIdsAsync(operatorId, ct);

    Task<IReadOnlyList<User>> ListByIdsIncludingDeletedAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default)
        => throw new NotSupportedException("Internal user batch lookup is not implemented by this repository.");

    Task<IReadOnlyList<Guid>> SearchUserIdsAsync(
        string search,
        int limit,
        CancellationToken ct = default)
        => throw new NotSupportedException("Internal user search is not implemented by this repository.");
}
