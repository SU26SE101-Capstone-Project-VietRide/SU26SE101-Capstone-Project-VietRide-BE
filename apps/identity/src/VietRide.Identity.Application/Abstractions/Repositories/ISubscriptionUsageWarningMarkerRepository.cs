using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface ISubscriptionUsageWarningMarkerRepository
    : IRepository<SubscriptionUsageWarningMarker, Guid>
{
    Task<bool> ExistsAsync(
        Guid subscriptionId,
        SubscriptionUsageResource resource,
        string periodKey,
        CancellationToken cancellationToken);
}
