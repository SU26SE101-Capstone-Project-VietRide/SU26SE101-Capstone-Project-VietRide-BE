using MediatR;

namespace VietRide.Identity.Application.Features.Admin.RejectOperator;

public sealed record RejectOperatorCommand(
    string CallerRole,
    Guid CallerUserId,
    Guid OperatorId,
    string Reason) : IRequest<RejectOperatorResponseDto>;
