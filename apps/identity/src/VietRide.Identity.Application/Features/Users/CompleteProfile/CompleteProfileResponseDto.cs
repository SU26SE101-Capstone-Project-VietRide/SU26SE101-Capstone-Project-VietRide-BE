namespace VietRide.Identity.Application.Features.Users.CompleteProfile;

/// <summary>Response DTO for POST /v1/users/me/complete-profile.</summary>
public sealed record CompleteProfileResponseDto(
    Guid UserId,
    string Phone,
    string Message);
