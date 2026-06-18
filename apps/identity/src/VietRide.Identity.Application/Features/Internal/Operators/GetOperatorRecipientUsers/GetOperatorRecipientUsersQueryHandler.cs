using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorRecipientUsers;

public sealed class GetOperatorRecipientUsersQueryHandler
    : IRequestHandler<GetOperatorRecipientUsersQuery, IReadOnlyList<Guid>>
{
    private readonly IUserRepository _userRepository;

    public GetOperatorRecipientUsersQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<IReadOnlyList<Guid>> Handle(
        GetOperatorRecipientUsersQuery request,
        CancellationToken cancellationToken)
        => _userRepository.ListActiveOperatorAdminIdsAsync(request.OperatorId, cancellationToken);
}
