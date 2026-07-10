using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed class GetInternalUsersQueryHandler(IUserRepository userRepository)
    : IRequestHandler<GetInternalUsersQuery, IReadOnlyList<GetInternalUserResponseDto>>
{
    public Task<IReadOnlyList<GetInternalUserResponseDto>> Handle(
        GetInternalUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = userRepository.QueryNoTracking()
            .Where(user => request.UserIds.Contains(user.Id))
            .ToList()
            .Select(user => new GetInternalUserResponseDto(
                user.Id,
                user.DisplayName,
                user.AvatarUrl,
                user.Role.ToString(),
                user.OperatorId,
                user.Status.ToString()))
            .ToList();

        return Task.FromResult<IReadOnlyList<GetInternalUserResponseDto>>(users);
    }
}
