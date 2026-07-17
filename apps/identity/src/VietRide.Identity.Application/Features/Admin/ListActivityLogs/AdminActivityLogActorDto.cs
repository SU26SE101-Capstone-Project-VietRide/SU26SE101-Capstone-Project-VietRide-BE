namespace VietRide.Identity.Application.Features.Admin.ListActivityLogs;

public sealed record AdminActivityLogActorDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role);
