using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Users.GetMe;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, GetMeResponseDto>
{
    private readonly IUserRepository _users;

    public GetMeQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<GetMeResponseDto> Handle(
        GetMeQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        return new GetMeResponseDto(
            Id: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Phone: user.Phone?.Value,
            Role: user.Role.ToString(),
            OperatorId: user.OperatorId,
            Status: user.Status.ToString(),
            AvatarUrl: user.AvatarUrl);
    }
}
