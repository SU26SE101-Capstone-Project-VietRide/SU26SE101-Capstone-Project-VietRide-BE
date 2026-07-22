using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface ISubscriptionUpgradeAttemptRepository : IRepository<SubscriptionUpgradeAttempt, Guid>
{
    Task<SubscriptionUpgradeAttempt?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SubscriptionUpgradeAttempt?> GetPendingBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionUpgradeAttempt?> GetActiveBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
        => GetPendingBySubscriptionIdAsync(subscriptionId, cancellationToken);

    Task<SubscriptionUpgradeAttempt?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    Task<IReadOnlyList<SubscriptionUpgradeAttempt>> ListDueAsync(
        SubscriptionUpgradeAttemptStatus status,
        DateTimeOffset dueBefore,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionUpgradeAttempt>> ListActiveAsync(
        int take,
        CancellationToken cancellationToken = default);
}
