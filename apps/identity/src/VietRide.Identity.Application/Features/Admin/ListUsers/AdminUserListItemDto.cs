namespace VietRide.Identity.Application.Features.Admin.ListUsers;

public sealed record AdminUserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    string? AvatarUrl,
    string Role,
    string Status,
    Guid? OperatorId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);
