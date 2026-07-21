using VietRide.Identity.Application.Features.Admin.OutboxDlq;

namespace VietRide.Identity.Application.Abstractions.ExternalClients;

public interface IAdminOutboxDlqSourceClient
{
    Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadAsync(
        string service,
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken cancellationToken = default);
}
