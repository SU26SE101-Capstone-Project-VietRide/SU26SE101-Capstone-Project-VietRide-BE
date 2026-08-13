using MediatR;

namespace VietRide.Identity.Application.Features.OperatorUsers.UnlockOperatorUser;

public sealed record UnlockOperatorUserCommand(
    Guid CallerUserId,
    string CallerRole,
    Guid? CallerOperatorId,
    Guid UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<UnlockOperatorUserResponseDto>;
