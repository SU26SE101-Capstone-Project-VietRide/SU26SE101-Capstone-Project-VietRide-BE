using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorSubscriptionRepository : IRepository<OperatorSubscription, Guid>
{
    Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<OperatorSubscription?> GetCurrentByOperatorIdForUpdateAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => GetCurrentByOperatorIdAsync(operatorId, cancellationToken);

    Task<OperatorSubscription?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => GetByIdAsync(id, cancellationToken);

    Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> GetCurrentWithPlanByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<(OperatorSubscription Subscription, SubscriptionPlan Plan)?>(null);

    Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> TryIncrementUsageWithinLimitAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        int delta,
        DateTimeOffset decisionAt,
        CancellationToken cancellationToken = default)
        => Task.FromResult<(OperatorSubscription Subscription, SubscriptionPlan Plan)?>(null);

    Task<bool> TryDecrementUsageAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task<bool> TryCreateOperatorUserWithinLimitAsync(
        Guid operatorId,
        User user,
        EmailVerificationToken initialPasswordToken,
        ActivityLog activityLog,
        UserRole role,
        DateTimeOffset decisionAt,
        CancellationToken cancellationToken = default);
}
