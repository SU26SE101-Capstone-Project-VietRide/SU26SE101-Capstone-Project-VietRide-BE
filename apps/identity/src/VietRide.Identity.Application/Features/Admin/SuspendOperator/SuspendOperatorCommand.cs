using MediatR;

namespace VietRide.Identity.Application.Features.Admin.SuspendOperator;

public sealed record SuspendOperatorCommand(
    string CallerRole,
    Guid CallerUserId,
    Guid OperatorId,
    string Reason) : IRequest<SuspendOperatorResponseDto>;
