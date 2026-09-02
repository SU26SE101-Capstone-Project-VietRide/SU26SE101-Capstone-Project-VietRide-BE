using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class ApproveSubscriptionCustomRequestCommandHandler
    : IRequestHandler<ApproveSubscriptionCustomRequestCommand, SubscriptionCustomRequestDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ApproveSubscriptionCustomRequestCommandHandler(
        ISubscriptionCustomRequestRepository requests,
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionPlanRepository plans,
        IActivityLogRepository activityLogs,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _requests = requests;
        _subscriptions = subscriptions;
        _plans = plans;
        _activityLogs = activityLogs;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<SubscriptionCustomRequestDto> Handle(
        ApproveSubscriptionCustomRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetByIdForUpdateAsync(command.RequestId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionCustomRequest), command.RequestId);
        if (request.Status != SubscriptionCustomRequestStatus.PENDING_REVIEW)
        {
            throw new CodedConflictException(
                "CUSTOM_REQUEST_ALREADY_REVIEWED",
                "The custom subscription request has already been reviewed.");
        }

        var subscription = await _subscriptions.GetCurrentByOperatorIdForUpdateAsync(
            request.OperatorId,
            cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
        var plan = SubscriptionPlan.CreateCustom(
            request.OperatorId,
            request.Id,
            command.Name,
            command.Description,
            Money.FromRaw(command.PricePerMonth),
            Money.FromRaw(command.PricePerYear),
            command.MaxVehicles,
            command.MaxDrivers,
            command.MaxAssistants,
            command.MaxOperatorUsers,
            command.MaxRoutes,
            command.MaxTripsPerMonth,
            command.EnableParcel,
            command.EnableShuttle,
            command.EnableRag);
        var violations = SubscriptionQuotaPolicy.GetLimitsBelowCurrentUsage(subscription, plan);
        if (violations.Count > 0)
        {
            throw new CodedValidationException(
                "CUSTOM_PLAN_LIMIT_BELOW_CURRENT_USAGE",
                "Granted custom plan limits cannot be below current usage.",
                violations.Select(violation => new ValidationError(
                    violation.Field,
                    $"requested {GetRequestedLimit(request, violation.Field)}, granted {violation.GrantedLimit}, current usage {violation.CurrentUsage}"))
                    .ToArray());
        }

        await _plans.AddAsync(plan, cancellationToken);
        request.Approve(command.CallerUserId, plan.Id, _clock.UtcNow);
        _requests.Update(request);
        await _activityLogs.AddAsync(
            ActivityLog.Create(
                command.CallerUserId,
                ActivityLogAction.APPROVE_SUBSCRIPTION_CUSTOM_REQUEST,
                JsonSerializer.Serialize(new
                {
                    requestId = request.Id,
                    operatorId = request.OperatorId,
                    planId = plan.Id,
                })),
            cancellationToken);
        var integrationEvent = new SubscriptionCustomRequestApprovedIntegrationEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            request.Id,
            request.OperatorId,
            plan.Id,
            plan.Name);
        await _outbox.EnqueueAsync(
            integrationEvent.EventId,
            SubscriptionCustomRequestApprovedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        return SubscriptionCustomRequestMapper.ToDto(request);
    }

    private static int GetRequestedLimit(SubscriptionCustomRequest request, string field)
        => field switch
        {
            "maxVehicles" => request.MaxVehicles,
            "maxDrivers" => request.MaxDrivers,
            "maxAssistants" => request.MaxAssistants,
            "maxOperatorUsers" => request.MaxOperatorUsers,
            "maxRoutes" => request.MaxRoutes,
            "maxTripsPerMonth" => request.MaxTripsPerMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
}
