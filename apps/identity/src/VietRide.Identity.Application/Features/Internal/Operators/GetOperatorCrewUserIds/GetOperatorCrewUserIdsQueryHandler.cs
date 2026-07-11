using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorCrewUserIds;

public sealed class GetOperatorCrewUserIdsQueryHandler
    : IRequestHandler<GetOperatorCrewUserIdsQuery, IReadOnlyList<Guid>>
{
    private readonly IUserRepository _userRepository;

    public GetOperatorCrewUserIdsQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<IReadOnlyList<Guid>> Handle(
        GetOperatorCrewUserIdsQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(
            _userRepository.QueryNoTracking()
                .Where(user =>
                    user.OperatorId == request.OperatorId
                    && (user.Role == UserRole.DRIVER || user.Role == UserRole.ASSISTANT)
                    && user.Status == UserStatus.ACTIVE)
                .OrderBy(user => user.Id)
                .Select(user => user.Id)
                .ToList());
}
