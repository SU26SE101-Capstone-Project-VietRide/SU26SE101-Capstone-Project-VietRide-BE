namespace VietRide.Identity.Application.Features.Auth.ResetPassword;

public sealed record ResetPasswordResponseDto(
    Guid UserId,
    string Status);
