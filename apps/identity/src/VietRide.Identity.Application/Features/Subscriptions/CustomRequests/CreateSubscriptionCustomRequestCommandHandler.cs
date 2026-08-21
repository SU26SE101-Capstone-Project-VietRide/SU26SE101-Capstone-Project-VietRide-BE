using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class CreateSubscriptionCustomRequestCommandHandler
    : IRequestHandler<CreateSubscriptionCustomRequestCommand, SubscriptionCustomRequestDto>
{
    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IActivityLogRepository _activityLogs;

    public CreateSubscriptionCustomRequestCommandHandler(
        ISubscriptionCustomRequestRepository requests,
        IOperatorSubscriptionRepository subscriptions,
        IActivityLogRepository activityLogs)
    {
        _requests = requests;
        _subscriptions = subscriptions;
        _activityLogs = activityLogs;
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
        return SubscriptionCustomRequestMapper.ToDto(request);
    }
}
