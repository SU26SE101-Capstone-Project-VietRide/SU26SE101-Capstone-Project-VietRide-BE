using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.ApproveOperator;

public sealed class ApproveOperatorCommandHandler : IRequestHandler<ApproveOperatorCommand, ApproveOperatorResponseDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;

    public ApproveOperatorCommandHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions,
        IActivityLogRepository activityLogs,
        IClock clock,
        IIntegrationEventOutbox outbox)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
        _activityLogs = activityLogs;
        _clock = clock;
        _outbox = outbox;
    }

    public async Task<ApproveOperatorResponseDto> Handle(
        ApproveOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can approve operators.");

        var operatorEntity = await _operators.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);
        var subscription = await _operatorSubscriptions.GetCurrentByOperatorIdAsync(request.OperatorId, cancellationToken)
            ?? throw new ValidationException(
                "Operator pending approval subscription was not found.",
                [new ValidationError(nameof(request.OperatorId), "Operator pending approval subscription was not found.")]);

        var approvedAt = _clock.UtcNow;
        TryApplyLifecycleTransition(() => operatorEntity.Approve(request.CallerUserId, approvedAt));
        TryApplyLifecycleTransition(() => subscription.ActivateTrial(approvedAt, approvedAt.AddDays(30)));

        var metadata = JsonSerializer.Serialize(new
        {
            operatorId = operatorEntity.Id,
            actorUserId = request.CallerUserId,
            source = "SYSTEM_ADMIN_APPROVE_OPERATOR",
        });

        await _activityLogs.AddAsync(
            ActivityLog.Create(request.CallerUserId, ActivityLogAction.APPROVE_OPERATOR, metadata),
            cancellationToken);

        // Enqueue the integration event inside the same transaction the
        // TransactionBehavior commits (BSOT §7.3).
        var integrationEvent = new OperatorApprovedIntegrationEvent(operatorEntity.Id, approvedAt);
        await _outbox.EnqueueAsync(
            OperatorApprovedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent),
            cancellationToken);

        return new ApproveOperatorResponseDto(operatorEntity.Id, operatorEntity.RegistrationStatus.ToString());
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
