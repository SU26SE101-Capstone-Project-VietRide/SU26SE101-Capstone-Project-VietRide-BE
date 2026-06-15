namespace VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;

public sealed record OperatorUserListItemDto(
    Guid UserId,
    string Email,
    string? Phone,
    string DisplayName,
    string Role,
    string Status,
    Guid OperatorId,
    DateTimeOffset CreatedAt,
    string? AvatarUrl);
