using System.Text.Json;

namespace VietRide.Identity.Application.Features.Admin.OutboxDlq;

public sealed record AdminOutboxDlqResponseDto(
    IReadOnlyList<AdminOutboxDlqResponseItemDto> Items,
    string? NextCursor,
    IReadOnlyList<string> UnavailableServices);

public sealed record AdminOutboxDlqResponseItemDto(
    string Service,
    Guid EventId,
    string EventType,
    JsonElement Payload,
    int RetryCount,
    string LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset TerminalAt);
