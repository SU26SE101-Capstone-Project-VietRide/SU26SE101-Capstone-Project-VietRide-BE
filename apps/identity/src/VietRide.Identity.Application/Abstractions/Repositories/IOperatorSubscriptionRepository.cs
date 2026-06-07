using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorSubscriptionRepository : IRepository<OperatorSubscription, Guid>
{
    Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);
}
