namespace VietRide.Identity.Application.Features.Auth.ChangePassword;

public sealed record ChangePasswordResponseDto(
    Guid UserId,
    bool SessionsRevoked);
