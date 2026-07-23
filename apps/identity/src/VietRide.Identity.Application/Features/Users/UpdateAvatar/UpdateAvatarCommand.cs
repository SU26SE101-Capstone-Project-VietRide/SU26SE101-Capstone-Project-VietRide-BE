using MediatR;

namespace VietRide.Identity.Application.Features.Users.UpdateAvatar;

public sealed record UpdateAvatarCommand(
    Guid UserId,
    string? AvatarUrl) : IRequest<UpdateAvatarResponse>;
