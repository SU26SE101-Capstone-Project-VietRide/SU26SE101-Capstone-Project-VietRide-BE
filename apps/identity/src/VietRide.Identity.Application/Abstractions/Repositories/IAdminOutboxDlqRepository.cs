using VietRide.Identity.Application.Features.Admin.OutboxDlq;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IAdminOutboxDlqRepository
{
    Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadAsync(
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken cancellationToken = default);
}
