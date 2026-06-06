namespace VietRide.Identity.Application.Features.Auth.ResendInitialPassword;

public sealed record ResendInitialPasswordResponseDto(
    Guid UserId,
    string Status,
    DateTimeOffset ExpiresAt);
