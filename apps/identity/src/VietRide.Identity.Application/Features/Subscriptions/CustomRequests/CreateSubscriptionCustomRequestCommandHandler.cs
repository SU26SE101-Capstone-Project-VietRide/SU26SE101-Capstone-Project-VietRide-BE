using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class CreateSubscriptionCustomRequestCommandHandler
    : IRequestHandler<CreateSubscriptionCustomRequestCommand, SubscriptionCustomRequestDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IOperatorRepository _operators;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public CreateSubscriptionCustomRequestCommandHandler(
        ISubscriptionCustomRequestRepository requests,
        IOperatorSubscriptionRepository subscriptions,
        IOperatorRepository operators,
        IActivityLogRepository activityLogs,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _requests = requests;
        _subscriptions = subscriptions;
        _operators = operators;
        _activityLogs = activityLogs;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<SubscriptionCustomRequestDto> Handle(
        CreateSubscriptionCustomRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (await _subscriptions.GetCurrentByOperatorIdAsync(command.OperatorId, cancellationToken) is null)
            throw new NotFoundException(nameof(OperatorSubscription), command.OperatorId);
        if (await _requests.GetPendingByOperatorIdAsync(command.OperatorId, cancellationToken) is not null)
        {
            throw new CodedConflictException(
                "CUSTOM_REQUEST_ALREADY_PENDING",
                "The operator already has a pending custom subscription request.");
        }
        var operatorTenant = await _operators.GetByIdNoTrackingAsync(command.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), command.OperatorId);

        var request = SubscriptionCustomRequest.Create(
            command.OperatorId,
            command.MaxVehicles,
            command.MaxDrivers,
            command.MaxAssistants,
            command.MaxOperatorUsers,
            command.MaxRoutes,
            command.MaxTripsPerMonth,
            command.EnableParcel,
            command.EnableShuttle,
            command.EnableRag,
            Enum.Parse<SubscriptionBillingPeriod>(command.PreferredBillingPeriod, ignoreCase: false),
            command.Note);
        await _requests.AddAsync(request, cancellationToken);
        await _activityLogs.AddAsync(
            ActivityLog.Create(
                command.CallerUserId,
                ActivityLogAction.CREATE_SUBSCRIPTION_CUSTOM_REQUEST,
                JsonSerializer.Serialize(new { requestId = request.Id, operatorId = command.OperatorId })),
            cancellationToken);
        var integrationEvent = new SubscriptionCustomRequestSubmittedIntegrationEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            request.Id,
            request.OperatorId,
            operatorTenant.Name);
        await _outbox.EnqueueAsync(
            integrationEvent.EventId,
            SubscriptionCustomRequestSubmittedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        return SubscriptionCustomRequestMapper.ToDto(request);
    }
}
