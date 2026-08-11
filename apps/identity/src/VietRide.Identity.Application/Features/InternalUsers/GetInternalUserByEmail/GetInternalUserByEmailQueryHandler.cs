using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByEmail;

public sealed class GetInternalUserByEmailQueryHandler
    : IRequestHandler<GetInternalUserByEmailQuery, GetInternalUserByEmailResponseDto>
{
    private readonly IUserRepository _users;

    public GetInternalUserByEmailQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<GetInternalUserByEmailResponseDto> Handle(
        GetInternalUserByEmailQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("User", normalizedEmail);

        return new GetInternalUserByEmailResponseDto(user.Id);
    }
}
