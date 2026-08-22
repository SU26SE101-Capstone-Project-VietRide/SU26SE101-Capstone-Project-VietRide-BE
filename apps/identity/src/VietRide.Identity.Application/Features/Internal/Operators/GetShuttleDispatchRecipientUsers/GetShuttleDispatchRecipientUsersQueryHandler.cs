using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetShuttleDispatchRecipientUsers;

public sealed class GetShuttleDispatchRecipientUsersQueryHandler
    : IRequestHandler<GetShuttleDispatchRecipientUsersQuery, IReadOnlyList<Guid>>
{
    private readonly IUserRepository _userRepository;

    public GetShuttleDispatchRecipientUsersQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<IReadOnlyList<Guid>> Handle(
        GetShuttleDispatchRecipientUsersQuery request,
        CancellationToken cancellationToken)
        => _userRepository.ListActiveShuttleDispatchRecipientIdsAsync(
            request.OperatorId,
            cancellationToken);
}
