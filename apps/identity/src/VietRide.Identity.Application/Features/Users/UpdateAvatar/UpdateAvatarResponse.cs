namespace VietRide.Identity.Application.Features.Users.UpdateAvatar;

public sealed record UpdateAvatarResponse(
    Guid UserId,
    string? AvatarUrl);
