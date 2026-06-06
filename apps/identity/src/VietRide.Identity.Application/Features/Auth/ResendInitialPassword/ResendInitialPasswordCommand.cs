using MediatR;

namespace VietRide.Identity.Application.Features.Auth.ResendInitialPassword;

public sealed record ResendInitialPasswordCommand(
    Guid UserId,
    Guid CallerUserId,
    string CallerRole,
    Guid? CallerOperatorId) : IRequest<ResendInitialPasswordResponseDto>;
