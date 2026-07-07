namespace VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;

/// <summary>Response DTO for POST /v1/auth/resend-verification-email (200).</summary>
public sealed record ResendVerificationEmailResponseDto(
    string Email,
    string Status,
    int OtpTtlMinutes);
