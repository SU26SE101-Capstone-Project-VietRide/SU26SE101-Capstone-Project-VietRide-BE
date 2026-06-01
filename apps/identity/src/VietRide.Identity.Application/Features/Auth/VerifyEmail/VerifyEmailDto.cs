namespace VietRide.Identity.Application.Features.Auth.VerifyEmail;

/// <summary>Response DTO for POST /v1/auth/verify-email (200).</summary>
public sealed record VerifyEmailResponseDto(
    Guid UserId,
    string Status);
