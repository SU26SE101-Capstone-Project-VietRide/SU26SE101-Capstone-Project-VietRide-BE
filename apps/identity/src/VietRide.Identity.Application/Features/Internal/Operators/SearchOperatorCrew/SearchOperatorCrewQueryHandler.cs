using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Internal.Operators.SearchOperatorCrew;

public sealed class SearchOperatorCrewQueryHandler(IUserRepository users)
    : IRequestHandler<SearchOperatorCrewQuery, IReadOnlyList<InternalOperatorCrewDto>>
{
    public Task<IReadOnlyList<InternalOperatorCrewDto>> Handle(
        SearchOperatorCrewQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.Search.Trim().ToLowerInvariant();
        IReadOnlyList<InternalOperatorCrewDto> result = users.QueryNoTracking()
            .Where(user => user.OperatorId == request.OperatorId
                && (user.Role == UserRole.DRIVER || user.Role == UserRole.ASSISTANT)
                && user.DisplayName.ToLower().Contains(search))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new InternalOperatorCrewDto(
                user.Id,
                user.DisplayName,
                user.Role.ToString()))
            .ToList();
        return Task.FromResult(result);
    }
}
