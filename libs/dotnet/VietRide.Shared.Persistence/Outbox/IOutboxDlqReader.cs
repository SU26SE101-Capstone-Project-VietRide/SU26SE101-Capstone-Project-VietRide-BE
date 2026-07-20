using System.Text.Json;

namespace VietRide.Shared.Persistence.Outbox;

public interface IOutboxDlqReader
{
    Task<IReadOnlyList<OutboxDlqReadItem>> ReadAsync(
        string? eventType,
        int pageSize,
        DateTimeOffset? afterTerminalAt,
        Guid? afterId,
        bool descending,
        CancellationToken ct = default);
}

public sealed record OutboxDlqReadItem(
    Guid EventId,
    string EventType,
    JsonElement Payload,
    int RetryCount,
    string LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset TerminalAt);
