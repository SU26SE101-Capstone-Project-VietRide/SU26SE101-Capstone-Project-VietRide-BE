using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Users.UpdateAvatar;

public sealed class UpdateAvatarCommandHandler
    : IRequestHandler<UpdateAvatarCommand, UpdateAvatarResponse>
{
    private readonly IUserRepository _users;

    public UpdateAvatarCommandHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<UpdateAvatarResponse> Handle(
        UpdateAvatarCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        user.UpdateAvatar(request.AvatarUrl);
        _users.Update(user);

        return new UpdateAvatarResponse(user.Id, user.AvatarUrl);
    }
}
