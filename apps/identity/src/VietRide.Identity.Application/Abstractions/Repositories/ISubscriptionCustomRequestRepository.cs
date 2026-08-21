using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface ISubscriptionCustomRequestRepository : IRepository<SubscriptionCustomRequest, Guid>
{
    Task<SubscriptionCustomRequest?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<SubscriptionCustomRequest?> GetPendingByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionCustomRequest>> ListForAdminAsync(
        SubscriptionCustomRequestStatus? status,
        CancellationToken cancellationToken = default);
}
