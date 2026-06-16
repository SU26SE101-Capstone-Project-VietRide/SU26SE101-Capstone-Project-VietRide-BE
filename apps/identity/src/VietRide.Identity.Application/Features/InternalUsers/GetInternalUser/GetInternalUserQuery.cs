using MediatR;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;

public sealed record GetInternalUserQuery(Guid UserId) : IRequest<GetInternalUserResponseDto>;
