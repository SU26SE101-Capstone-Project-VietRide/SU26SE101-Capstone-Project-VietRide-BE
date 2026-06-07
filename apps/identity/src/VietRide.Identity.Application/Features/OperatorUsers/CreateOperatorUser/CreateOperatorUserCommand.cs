using MediatR;

namespace VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;

public sealed record CreateOperatorUserCommand(
    string Email,
    string Phone,
    string DisplayName,
    string Role,
    Guid CallerUserId,
    string CallerRole,
    Guid? CallerOperatorId) : IRequest<CreateOperatorUserResponseDto>;
