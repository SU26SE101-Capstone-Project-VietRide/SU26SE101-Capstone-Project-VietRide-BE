using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace VietRide.Shared.Persistence.Outbox;

public sealed class OutboxDlqReader : IOutboxDlqReader
{
    private readonly VietRideDbContextBase _db;

    public OutboxDlqReader(VietRideDbContextBase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<OutboxDlqReadItem>> ReadAsync(
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.OutboxDlq.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(row => row.EventType == eventType.Trim());

        if (afterTerminalAt.HasValue && afterId.HasValue)
        {
            query = descending
                ? query.Where(row => row.TerminalAt < afterTerminalAt.Value
                    || (row.TerminalAt == afterTerminalAt.Value && row.EventId.CompareTo(afterId.Value) < 0))
                : query.Where(row => row.TerminalAt > afterTerminalAt.Value
                    || (row.TerminalAt == afterTerminalAt.Value && row.EventId.CompareTo(afterId.Value) > 0));
        }

        query = descending
            ? query.OrderByDescending(row => row.TerminalAt).ThenByDescending(row => row.EventId)
            : query.OrderBy(row => row.TerminalAt).ThenBy(row => row.EventId);

        var rows = await query.Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(row => new OutboxDlqReadItem(
            row.EventId,
            row.EventType,
            ParsePayload(row.Payload),
            row.RetryCount,
            row.LastError,
            row.CreatedAt,
            row.TerminalAt)).ToArray();
    }

    private static JsonElement ParsePayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
