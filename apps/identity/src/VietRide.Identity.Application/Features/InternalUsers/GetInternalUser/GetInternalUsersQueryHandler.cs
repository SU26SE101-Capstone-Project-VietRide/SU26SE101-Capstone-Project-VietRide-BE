using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed class GetInternalUsersQueryHandler(IUserRepository userRepository)
    : IRequestHandler<GetInternalUsersQuery, IReadOnlyList<GetInternalUserResponseDto>>
{
    public async Task<IReadOnlyList<GetInternalUserResponseDto>> Handle(
        GetInternalUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = await userRepository.ListByIdsIncludingDeletedAsync(
            request.UserIds,
            cancellationToken);
        var byId = users.ToDictionary(user => user.Id);
        return request.UserIds
            .Where(byId.ContainsKey)
            .Select(userId => Map(byId[userId]))
            .ToArray();
    }

    private static GetInternalUserResponseDto Map(User user)
    {
        var deleted = user.DeletedAt.HasValue;
        return new GetInternalUserResponseDto(
            user.Id,
            deleted ? "Người dùng đã xóa" : user.DisplayName,
            deleted ? null : user.AvatarUrl,
            user.Role.ToString(),
            user.OperatorId,
            user.Status.ToString(),
            deleted ? null : user.Phone?.Value,
            deleted ? null : user.Email,
            deleted);
    }
}
