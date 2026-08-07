using MediatR;

namespace VietRide.Identity.Application.Features.Admin.ReactivateOperator;

public sealed record ReactivateOperatorCommand(
    string CallerRole,
    Guid CallerUserId,
    Guid OperatorId) : IRequest<ReactivateOperatorResponseDto>;
