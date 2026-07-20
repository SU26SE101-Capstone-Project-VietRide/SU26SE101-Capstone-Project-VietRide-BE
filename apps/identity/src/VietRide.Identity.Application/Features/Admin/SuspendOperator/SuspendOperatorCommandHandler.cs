using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.SuspendOperator;

public sealed class SuspendOperatorCommandHandler : IRequestHandler<SuspendOperatorCommand, SuspendOperatorResponseDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IUserRepository _users;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;

    public SuspendOperatorCommandHandler(
        IOperatorRepository operators,
        IUserRepository users,
        IClock clock,
        IIntegrationEventOutbox outbox)
    {
        _operators = operators;
        _users = users;
        _clock = clock;
        _outbox = outbox;
    }

    public async Task<SuspendOperatorResponseDto> Handle(
        SuspendOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can suspend operators.");

        var operatorEntity = await _operators.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        var suspendedAt = _clock.UtcNow;
        TryApplyLifecycleTransition(() => operatorEntity.Suspend(request.Reason, suspendedAt));

        // Enqueue the integration event inside the same transaction the
        // TransactionBehavior commits (BSOT §7.3).
        var integrationEvent = new OperatorSuspendedIntegrationEvent(operatorEntity.Id, suspendedAt);
        await _outbox.EnqueueAsync(
            OperatorSuspendedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent),
            cancellationToken);

        var operatorAdminIds = await _users.ListOperatorAdminIdsAsync(
            operatorEntity.Id,
            cancellationToken);
        foreach (var userId in operatorAdminIds)
        {
            var firebaseEvent = new FirebaseSessionRevocationRequestedIntegrationEvent(
                Guid.NewGuid(),
                suspendedAt,
                userId,
                "OPERATOR_SUSPENDED");
            await _outbox.EnqueueAsync(
                firebaseEvent.EventId,
                FirebaseSessionRevocationRequestedIntegrationEvent.EventType,
                JsonSerializer.Serialize(firebaseEvent),
                cancellationToken);
        }

        return new SuspendOperatorResponseDto(operatorEntity.Id, operatorEntity.RegistrationStatus.ToString());
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
