using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.OutboxDlq;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class AdminOutboxDlqRepository : IAdminOutboxDlqRepository
{
    private readonly IOutboxDlqReader _reader;

    public AdminOutboxDlqRepository(IOutboxDlqReader reader)
    {
        _reader = reader;
    }

    public async Task<IReadOnlyList<AdminOutboxDlqItemDto>> ReadAsync(
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reader.ReadAsync(
            eventType,
            pageSize,
            afterTerminalAt,
            afterId,
            descending,
            cancellationToken).ConfigureAwait(false);

        return rows.Select(row => new AdminOutboxDlqItemDto(
            "identity",
            row.EventId,
            row.EventType,
            row.Payload,
            row.RetryCount,
            row.LastError,
            row.CreatedAt,
            row.TerminalAt)).ToArray();
    }
}
