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
            .Where(x => x.Status != SubscriptionStatus.CANCELLED)
            .OrderByDescending(x => x.StartedAt ?? x.LastResetAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<OperatorSubscription?> GetCurrentByOperatorIdForUpdateAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.OperatorSubscriptions
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.operator_subscriptions WHERE operator_id = {operatorId} AND status <> 'CANCELLED' ORDER BY COALESCE(started_at, last_reset_at) DESC LIMIT 1 FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<OperatorSubscription?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.OperatorSubscriptions
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.operator_subscriptions WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> GetCurrentWithPlanByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.OperatorSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.OperatorId == operatorId)
            .Join(
                _dbContext.SubscriptionPlans.AsNoTracking(),
                subscription => subscription.PlanId,
                plan => plan.Id,
                (subscription, plan) => new { Subscription = subscription, Plan = plan })
            .OrderByDescending(x => x.Subscription.StartedAt ?? x.Subscription.LastResetAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.Subscription, row.Plan);
    }

    public async Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> TryIncrementUsageWithinLimitAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        int delta,
        CancellationToken cancellationToken = default)
    {
        var subscriptionId = await _dbContext.OperatorSubscriptions
            .Where(x => x.OperatorId == operatorId)
            .Where(x => x.Status == SubscriptionStatus.ACTIVE)
            .OrderByDescending(x => x.StartedAt ?? x.LastResetAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscriptionId == Guid.Empty)
        {
            return null;
        }

        var updatedRows = resource switch
        {
            SubscriptionUsageResource.VEHICLES => await IncrementVehicleCountAsync(subscriptionId, delta, cancellationToken),
            SubscriptionUsageResource.DRIVERS => await IncrementDriverCountAsync(subscriptionId, delta, cancellationToken),
            SubscriptionUsageResource.ASSISTANTS => await IncrementAssistantCountAsync(subscriptionId, delta, cancellationToken),
            SubscriptionUsageResource.OPERATOR_USERS => await IncrementOperatorUserCountAsync(subscriptionId, delta, cancellationToken),
            SubscriptionUsageResource.ROUTES => await IncrementRouteCountAsync(subscriptionId, delta, cancellationToken),
            SubscriptionUsageResource.TRIPS_THIS_MONTH => await IncrementTripCountAsync(subscriptionId, delta, cancellationToken),
            _ => 0,
        };

        return updatedRows == 1
            ? await GetCurrentWithPlanByOperatorIdAsync(operatorId, cancellationToken)
            : null;
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
            UserRole.DRIVER => await IncrementDriverCountAsync(subscriptionId, 1, cancellationToken),
            UserRole.ASSISTANT => await IncrementAssistantCountAsync(subscriptionId, 1, cancellationToken),
            UserRole.OPERATOR_STAFF => await IncrementOperatorUserCountAsync(subscriptionId, 1, cancellationToken),
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

    public async Task<bool> TryDecrementUsageAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        CancellationToken cancellationToken = default)
    {
        var subscriptionId = await _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.OperatorId == operatorId)
            .Select(subscription => subscription.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscriptionId == Guid.Empty)
            return false;

        var rows = resource switch
        {
            SubscriptionUsageResource.VEHICLES => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentVehicles), cancellationToken),
            SubscriptionUsageResource.DRIVERS => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentDrivers), cancellationToken),
            SubscriptionUsageResource.ASSISTANTS => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentAssistants), cancellationToken),
            SubscriptionUsageResource.OPERATOR_USERS => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentOperatorUsers), cancellationToken),
            SubscriptionUsageResource.ROUTES => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentRoutes), cancellationToken),
            SubscriptionUsageResource.TRIPS_THIS_MONTH => await DecrementAsync(subscriptionId, nameof(OperatorSubscription.CurrentTripsThisMonth), cancellationToken),
            _ => 0,
        };
        return rows == 1;
    }

    private Task<int> DecrementAsync(Guid subscriptionId, string propertyName, CancellationToken cancellationToken)
    {
        return propertyName switch
        {
            nameof(OperatorSubscription.CurrentVehicles) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentVehicles > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentVehicles, x => x.CurrentVehicles - 1), cancellationToken),
            nameof(OperatorSubscription.CurrentDrivers) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentDrivers > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentDrivers, x => x.CurrentDrivers - 1), cancellationToken),
            nameof(OperatorSubscription.CurrentAssistants) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentAssistants > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentAssistants, x => x.CurrentAssistants - 1), cancellationToken),
            nameof(OperatorSubscription.CurrentOperatorUsers) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentOperatorUsers > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentOperatorUsers, x => x.CurrentOperatorUsers - 1), cancellationToken),
            nameof(OperatorSubscription.CurrentRoutes) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentRoutes > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentRoutes, x => x.CurrentRoutes - 1), cancellationToken),
            nameof(OperatorSubscription.CurrentTripsThisMonth) => _dbContext.OperatorSubscriptions.Where(x => x.Id == subscriptionId && x.CurrentTripsThisMonth > 0)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CurrentTripsThisMonth, x => x.CurrentTripsThisMonth - 1), cancellationToken),
            _ => Task.FromResult(0),
        };
    }

    private Task<int> IncrementVehicleCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentVehicles + delta <= plan.MaxVehicles))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentVehicles,
                    subscription => subscription.CurrentVehicles + delta),
                cancellationToken);

    private Task<int> IncrementDriverCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentDrivers + delta <= plan.MaxDrivers))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentDrivers,
                    subscription => subscription.CurrentDrivers + delta),
                cancellationToken);

    private Task<int> IncrementAssistantCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentAssistants + delta <= plan.MaxAssistants))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentAssistants,
                    subscription => subscription.CurrentAssistants + delta),
                cancellationToken);

    private Task<int> IncrementOperatorUserCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentOperatorUsers + delta <= plan.MaxOperatorUsers))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentOperatorUsers,
                    subscription => subscription.CurrentOperatorUsers + delta),
                cancellationToken);

    private Task<int> IncrementRouteCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentRoutes + delta <= plan.MaxRoutes))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentRoutes,
                    subscription => subscription.CurrentRoutes + delta),
                cancellationToken);

    private Task<int> IncrementTripCountAsync(Guid subscriptionId, int delta, CancellationToken cancellationToken)
        => _dbContext.OperatorSubscriptions
            .Where(subscription => subscription.Id == subscriptionId)
            .Where(subscription => _dbContext.SubscriptionPlans.Any(plan =>
                plan.Id == subscription.PlanId && subscription.CurrentTripsThisMonth + delta <= plan.MaxTripsPerMonth))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    subscription => subscription.CurrentTripsThisMonth,
                    subscription => subscription.CurrentTripsThisMonth + delta),
                cancellationToken);
}
