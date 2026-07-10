using MediatR;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed record GetInternalUsersQuery(IReadOnlyCollection<Guid> UserIds)
    : IRequest<IReadOnlyList<GetInternalUserResponseDto>>;
