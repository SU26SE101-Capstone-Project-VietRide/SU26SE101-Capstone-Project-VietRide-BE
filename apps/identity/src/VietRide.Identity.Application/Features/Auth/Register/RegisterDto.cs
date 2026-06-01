namespace VietRide.Identity.Application.Features.Auth.Register;

/// <summary>Response DTO for POST /v1/auth/register (201).</summary>
public sealed record RegisterResponseDto(
    Guid UserId,
    string Email,
    string Status,
    int OtpTtlMinutes);
