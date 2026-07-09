using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelPricingPolicyRepository : IParcelPricingPolicyRepository
{
    private readonly ParcelDbContext dbContext;

    public ParcelPricingPolicyRepository(ParcelDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<decimal> GetSystemDecimalAsync(
        string key,
        decimal fallback,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = key.Trim().ToUpperInvariant();
        var value = await dbContext.SystemConfigs.AsNoTracking()
            .Where(config => config.Key == normalized
                && config.IsActive
                && config.EffectiveFrom <= now
                && (config.EffectiveTo == null || config.EffectiveTo > now))
            .OrderByDescending(config => config.Version)
            .Select(config => (decimal?)config.DecimalValue)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? fallback;
    }

    public async Task<decimal> GetDepositPercentAsync(
        Guid operatorId,
        Guid routeId,
        decimal fallback,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var value = await dbContext.OperatorDepositPolicies.AsNoTracking()
            .Where(policy => policy.OperatorId == operatorId
                && (policy.RouteId == routeId || policy.RouteId == null)
                && policy.IsActive
                && policy.EffectiveFrom <= now
                && (policy.EffectiveTo == null || policy.EffectiveTo > now))
            .OrderByDescending(policy => policy.RouteId == routeId)
            .ThenByDescending(policy => policy.EffectiveFrom)
            .Select(policy => (decimal?)policy.DepositPercent)
            .FirstOrDefaultAsync(cancellationToken);

        return value ?? fallback;
    }
}
