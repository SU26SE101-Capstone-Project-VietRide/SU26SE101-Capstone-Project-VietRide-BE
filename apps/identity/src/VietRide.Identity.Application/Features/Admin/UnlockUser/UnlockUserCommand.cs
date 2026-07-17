using MediatR;

namespace VietRide.Identity.Application.Features.Admin.UnlockUser;

public sealed record UnlockUserCommand(
    Guid CallerUserId,
    string CallerRole,
    Guid UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<UnlockUserResponseDto>;
