using System.Text.Json;

namespace VietRide.Identity.Application.Features.Admin.ListActivityLogs;

public sealed record AdminActivityLogItemDto(
    Guid Id,
    AdminActivityLogActorDto Actor,
    string Action,
    JsonElement? Metadata,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);
