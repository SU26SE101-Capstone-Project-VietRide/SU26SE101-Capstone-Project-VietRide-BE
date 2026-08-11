using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Admin.ReactivateOperator;

public sealed class ReactivateOperatorCommandHandler
    : IRequestHandler<ReactivateOperatorCommand, ReactivateOperatorResponseDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IActivityLogRepository _activityLogs;
    private readonly ILogger<ReactivateOperatorCommandHandler> _logger;

    public ReactivateOperatorCommandHandler(
        IOperatorRepository operators,
        IActivityLogRepository activityLogs,
        ILogger<ReactivateOperatorCommandHandler>? logger = null)
    {
        _operators = operators;
        _activityLogs = activityLogs;
        _logger = logger ?? NullLogger<ReactivateOperatorCommandHandler>.Instance;
    }

    public async Task<ReactivateOperatorResponseDto> Handle(
        ReactivateOperatorCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can reactivate operators.");

        var operatorEntity = await _operators.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        try
        {
            operatorEntity.Reactivate();
        }
        catch (InvalidOperationException exception)
        {
            throw new ValidationException(
                exception.Message,
                [new ValidationError("registrationStatus", exception.Message)]);
        }

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                request.CallerUserId,
                ActivityLogAction.REACTIVATE_OPERATOR,
                JsonSerializer.Serialize(new
                {
                    operatorId = operatorEntity.Id,
                    actorUserId = request.CallerUserId,
                    source = "SYSTEM_ADMIN_REACTIVATE_OPERATOR",
                })),
            cancellationToken);

        _logger.LogInformation(
            "OperatorReactivated: operator {OperatorId} was reactivated by actor {ActorUserId}",
            operatorEntity.Id,
            request.CallerUserId);

        return new ReactivateOperatorResponseDto(
            operatorEntity.Id,
            operatorEntity.RegistrationStatus.ToString(),
            operatorEntity.IsActive);
    }
}
