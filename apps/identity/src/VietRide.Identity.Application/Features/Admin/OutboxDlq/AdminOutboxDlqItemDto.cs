using System.Text.Json;

namespace VietRide.Identity.Application.Features.Admin.OutboxDlq;

public sealed record AdminOutboxDlqItemDto(
    string Service,
    Guid EventId,
    string EventType,
    JsonElement Payload,
    int RetryCount,
    string LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset TerminalAt);
