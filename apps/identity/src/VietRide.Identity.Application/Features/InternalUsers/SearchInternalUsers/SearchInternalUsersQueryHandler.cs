using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.InternalUsers.SearchInternalUsers;

public sealed class SearchInternalUsersQueryHandler(IUserRepository users)
    : IRequestHandler<SearchInternalUsersQuery, SearchInternalUsersResponseDto>
{
    private const int MaximumMatches = 1000;

    public async Task<SearchInternalUsersResponseDto> Handle(
        SearchInternalUsersQuery request,
        CancellationToken cancellationToken)
    {
        var ids = await users.SearchUserIdsAsync(
            request.Search.Trim(), MaximumMatches + 1, cancellationToken);
        if (ids.Count > MaximumMatches)
        {
            throw new CodedValidationException(
                "SEARCH_TOO_BROAD",
                "Search matched more than 1,000 users. Narrow the search term.");
        }

        return new SearchInternalUsersResponseDto(ids);
    }
}
