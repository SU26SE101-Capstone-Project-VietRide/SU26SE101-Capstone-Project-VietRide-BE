namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed record GetInternalUserResponseDto(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    Guid? OperatorId,
    string Status,
    string? Phone = null,
    string? Email = null);
