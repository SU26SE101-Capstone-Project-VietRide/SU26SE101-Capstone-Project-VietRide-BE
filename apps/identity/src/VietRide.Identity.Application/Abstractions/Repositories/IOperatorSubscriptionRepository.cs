using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorSubscriptionRepository : IRepository<OperatorSubscription, Guid>
{
    Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<bool> TryCreateOperatorUserWithinLimitAsync(
        Guid operatorId,
        User user,
        EmailVerificationToken initialPasswordToken,
        ActivityLog activityLog,
        UserRole role,
        CancellationToken cancellationToken = default);
}
