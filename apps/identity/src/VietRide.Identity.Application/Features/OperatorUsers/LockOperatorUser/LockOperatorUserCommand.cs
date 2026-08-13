using MediatR;

namespace VietRide.Identity.Application.Features.OperatorUsers.LockOperatorUser;

public sealed record LockOperatorUserCommand(
    Guid CallerUserId,
    string CallerRole,
    Guid? CallerOperatorId,
    Guid UserId,
    string? IpAddress,
    string? UserAgent) : IRequest<LockOperatorUserResponseDto>;
