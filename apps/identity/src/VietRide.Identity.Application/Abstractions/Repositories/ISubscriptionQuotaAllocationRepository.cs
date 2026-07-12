using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface ISubscriptionQuotaAllocationRepository : IRepository<SubscriptionQuotaAllocation, Guid>
{
    Task<SubscriptionQuotaAllocation?> GetActiveAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        Guid resourceId,
        CancellationToken cancellationToken = default);

    Task AcquireLockAsync(
        Guid operatorId,
        SubscriptionUsageResource resource,
        Guid resourceId,
        CancellationToken cancellationToken = default);
}
