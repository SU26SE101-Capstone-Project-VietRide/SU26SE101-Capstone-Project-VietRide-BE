using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByPhone;

public sealed class GetInternalUserByPhoneQueryHandler
    : IRequestHandler<GetInternalUserByPhoneQuery, GetInternalUserByPhoneResponseDto>
{
    private readonly IUserRepository _users;

    public GetInternalUserByPhoneQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<GetInternalUserByPhoneResponseDto> Handle(
        GetInternalUserByPhoneQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByPhoneAsync(request.Phone, cancellationToken)
            ?? throw new NotFoundException("User", request.Phone);

        return new GetInternalUserByPhoneResponseDto(user.Id);
    }
}
