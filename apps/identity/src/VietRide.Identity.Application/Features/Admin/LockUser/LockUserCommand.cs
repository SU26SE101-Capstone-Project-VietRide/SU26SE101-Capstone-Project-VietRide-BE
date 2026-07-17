using MediatR;

namespace VietRide.Identity.Application.Features.Admin.LockUser;

public sealed record LockUserCommand(
    Guid CallerUserId,
    string CallerRole,
    Guid UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<LockUserResponseDto>;
