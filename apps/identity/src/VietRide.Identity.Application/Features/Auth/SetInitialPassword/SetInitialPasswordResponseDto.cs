namespace VietRide.Identity.Application.Features.Auth.SetInitialPassword;

public sealed record SetInitialPasswordResponseDto(
    Guid UserId,
    string Status);
