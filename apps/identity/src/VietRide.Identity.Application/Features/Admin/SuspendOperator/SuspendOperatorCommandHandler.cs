using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Admin.SuspendOperator;

public sealed class SuspendOperatorCommandHandler : IRequestHandler<SuspendOperatorCommand, SuspendOperatorResponseDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IClock _clock;

    public SuspendOperatorCommandHandler(IOperatorRepository operators, IClock clock)
    {
        _operators = operators;
        _clock = clock;
    }

    public async Task<SuspendOperatorResponseDto> Handle(
        SuspendOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can suspend operators.");

        var operatorEntity = await _operators.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        TryApplyLifecycleTransition(() => operatorEntity.Suspend(request.Reason, _clock.UtcNow));

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
