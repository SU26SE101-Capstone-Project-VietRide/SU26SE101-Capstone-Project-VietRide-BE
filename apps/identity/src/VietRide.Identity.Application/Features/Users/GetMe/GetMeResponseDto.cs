namespace VietRide.Identity.Application.Features.Users.GetMe;

/// <summary>Response DTO for GET /v1/users/me.</summary>
public sealed record GetMeResponseDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    string Role,
    Guid? OperatorId,
    string Status,
    string? AvatarUrl);
