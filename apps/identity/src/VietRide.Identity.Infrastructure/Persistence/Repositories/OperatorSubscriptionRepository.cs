using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class OperatorSubscriptionRepository : IOperatorSubscriptionRepository
{
    private readonly IdentityDbContext _dbContext;

    public OperatorSubscriptionRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.OperatorSubscriptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<OperatorSubscription> AddAsync(
        OperatorSubscription entity,
        CancellationToken cancellationToken = default)
    {
        _dbContext.OperatorSubscriptions.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorSubscription entity)
        => _dbContext.OperatorSubscriptions.Update(entity);

    public void Remove(OperatorSubscription entity)
        => _dbContext.OperatorSubscriptions.Remove(entity);

    public IQueryable<OperatorSubscription> Query()
        => _dbContext.OperatorSubscriptions;

    public IQueryable<OperatorSubscription> QueryNoTracking()
        => _dbContext.OperatorSubscriptions.AsNoTracking();

    public Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.OperatorSubscriptions
            .Where(x => x.OperatorId == operatorId)
            .Where(x => x.Status == SubscriptionStatus.PENDING_APPROVAL || x.Status == SubscriptionStatus.ACTIVE)
            .OrderByDescending(x => x.StartedAt ?? x.LastResetAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryCreateOperatorUserWithinLimitAsync(
        Guid operatorId,
        User user,
        EmailVerificationToken initialPasswordToken,
        ActivityLog activityLog,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var subscriptionId = await _dbContext.OperatorSubscriptions
            .Where(x => x.OperatorId == operatorId)
            .Where(x => x.Status == SubscriptionStatus.PENDING_APPROVAL || x.Status == SubscriptionStatus.ACTIVE)
            .OrderByDescending(x => x.StartedAt ?? x.LastResetAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscriptionId == Guid.Empty)
        {
            return false;
        }

        var updatedRows = role switch
        {
            UserRole.DRIVER => await IncrementDriverCountAsync(subscriptionId, cancellationToken),
            UserRole.ASSISTANT => await IncrementAssistantCountAsync(subscriptionId, cancellationToken),
            UserRole.OPERATOR_STAFF => await IncrementOperatorUserCountAsync(subscriptionId, cancellationToken),
            _ => 0,
        };

        if (updatedRows != 1)
        {
            return false;
        }

        await _dbContext.Users.AddAsync(user, cancellationToken);
        await _dbContext.EmailVerificationTokens.AddAsync(initialPasswordToken, cancellationToken);
        await _dbContext.ActivityLogs.AddAsync(activityLog, cancellationToken);

        return true;
    }

    private Task<int> IncrementDriverCountAsync(Guid subscriptionId, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentDrivers < plan.MaxDrivers))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentDrivers,
                    subscription => subscription.CurrentDrivers + 1),
                cancellationToken);

    private Task<int> IncrementAssistantCountAsync(Guid subscriptionId, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentAssistants < plan.MaxAssistants))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentAssistants,
                    subscription => subscription.CurrentAssistants + 1),
                cancellationToken);

    private Task<int> IncrementOperatorUserCountAsync(Guid subscriptionId, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentOperatorUsers < plan.MaxOperatorUsers))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentOperatorUsers,
                    subscription => subscription.CurrentOperatorUsers + 1),
                cancellationToken);
}
