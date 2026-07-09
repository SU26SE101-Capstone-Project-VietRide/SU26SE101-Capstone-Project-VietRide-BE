namespace VietRide.Identity.Application.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordResponseDto(
    string Email,
    int OtpTtlMinutes);
