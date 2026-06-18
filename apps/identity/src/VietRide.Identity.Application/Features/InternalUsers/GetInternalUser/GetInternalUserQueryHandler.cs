using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed class GetInternalUserQueryHandler : IRequestHandler<GetInternalUserQuery, GetInternalUserResponseDto>
{
    private readonly IUserRepository _userRepository;

    public GetInternalUserQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<GetInternalUserResponseDto> Handle(GetInternalUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        return new GetInternalUserResponseDto(
            user.Id,
            user.Role.ToString(),
            user.OperatorId,
            user.Status.ToString());
    }
}
