using MediatR;

namespace VietRide.Identity.Application.Features.InternalUsers.SearchInternalUsers;

public sealed record SearchInternalUsersQuery(string Search)
    : IRequest<SearchInternalUsersResponseDto>;
