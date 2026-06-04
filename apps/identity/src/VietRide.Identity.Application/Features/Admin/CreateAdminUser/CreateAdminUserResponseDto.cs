namespace VietRide.Identity.Application.Features.Admin.CreateAdminUser;

/// <summary>Response DTO for POST /v1/admin/users.</summary>
public sealed record CreateAdminUserResponseDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string Status);
