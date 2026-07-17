using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IActivityLogRepository
{
    Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct = default);

    Task<bool> ExistsBySourceEventIdAsync(Guid sourceEventId, CancellationToken ct = default)
        => Task.FromResult(false);

    Task<PagedResult<ActivityLog>> ListAsync(
        QueryOptions options,
        Guid? actorUserId,
        ActivityLogAction? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
        => throw new NotSupportedException("Activity log listing is not implemented by this repository.");
}
