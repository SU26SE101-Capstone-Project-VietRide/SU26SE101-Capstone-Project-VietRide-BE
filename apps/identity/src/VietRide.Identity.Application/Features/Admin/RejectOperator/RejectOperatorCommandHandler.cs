using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.RejectOperator;

public sealed class RejectOperatorCommandHandler : IRequestHandler<RejectOperatorCommand, RejectOperatorResponseDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IClock _clock;

    public RejectOperatorCommandHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions,
        IActivityLogRepository activityLogs,
        IClock clock)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
        _activityLogs = activityLogs;
        _clock = clock;
    }

    public async Task<RejectOperatorResponseDto> Handle(
        RejectOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can reject operators.");

        var operatorEntity = await _operators.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);
        var subscription = await _operatorSubscriptions.GetCurrentByOperatorIdAsync(request.OperatorId, cancellationToken)
            ?? throw new ValidationException(
                "Operator pending approval subscription was not found.",
                [new ValidationError(nameof(request.OperatorId), "Operator pending approval subscription was not found.")]);

        var rejectedAt = _clock.UtcNow;
        TryApplyLifecycleTransition(() => operatorEntity.Reject(request.CallerUserId, request.Reason, rejectedAt));
        TryApplyLifecycleTransition(subscription.CancelPendingApproval);

        var metadata = JsonSerializer.Serialize(new
        {
            operatorId = operatorEntity.Id,
            actorUserId = request.CallerUserId,
            reason = operatorEntity.RejectReason,
            source = "SYSTEM_ADMIN_REJECT_OPERATOR",
        });

        await _activityLogs.AddAsync(
            ActivityLog.Create(request.CallerUserId, ActivityLogAction.REJECT_OPERATOR, metadata),
            cancellationToken);

        return new RejectOperatorResponseDto(operatorEntity.Id, operatorEntity.RegistrationStatus.ToString());
    }

    private static void TryApplyLifecycleTransition(Action transition)
    {
        try
        {
            transition();
        }
        catch (InvalidOperationException exception)
        {
            throw new ValidationException(
                exception.Message,
                [new ValidationError("registrationStatus", exception.Message)]);
        }
    }
}
