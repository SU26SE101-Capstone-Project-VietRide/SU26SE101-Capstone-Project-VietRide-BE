using MediatR;

namespace VietRide.Identity.Application.Features.Admin.ApproveOperator;

public sealed record ApproveOperatorCommand(
    string CallerRole,
    Guid CallerUserId,
    Guid OperatorId) : IRequest<ApproveOperatorResponseDto>;
