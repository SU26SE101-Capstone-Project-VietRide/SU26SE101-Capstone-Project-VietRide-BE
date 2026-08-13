using MediatR;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByEmail;

public sealed record GetInternalUserByEmailQuery(string Email)
    : IRequest<GetInternalUserByEmailResponseDto>;
