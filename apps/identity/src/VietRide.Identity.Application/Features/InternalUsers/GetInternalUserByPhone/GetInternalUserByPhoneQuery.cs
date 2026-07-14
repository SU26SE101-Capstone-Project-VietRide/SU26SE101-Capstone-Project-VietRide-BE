using MediatR;

namespace VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByPhone;

public sealed record GetInternalUserByPhoneQuery(string Phone)
    : IRequest<GetInternalUserByPhoneResponseDto>;
