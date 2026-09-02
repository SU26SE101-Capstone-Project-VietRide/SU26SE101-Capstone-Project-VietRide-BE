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

public sealed class RejectSubscriptionCustomRequestCommandHandler
    : IRequestHandler<RejectSubscriptionCustomRequestCommand, SubscriptionCustomRequestDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IActivityLogRepository _activityLogs;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public RejectSubscriptionCustomRequestCommandHandler(
        ISubscriptionCustomRequestRepository requests,
        IActivityLogRepository activityLogs,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _requests = requests;
        _activityLogs = activityLogs;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<SubscriptionCustomRequestDto> Handle(
        RejectSubscriptionCustomRequestCommand command,
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

        request.Reject(command.CallerUserId, command.Reason, _clock.UtcNow);
        _requests.Update(request);
        await _activityLogs.AddAsync(
            ActivityLog.Create(
                command.CallerUserId,
                ActivityLogAction.REJECT_SUBSCRIPTION_CUSTOM_REQUEST,
                JsonSerializer.Serialize(new
                {
                    requestId = request.Id,
                    operatorId = request.OperatorId,
                    reason = request.RejectionReason,
                })),
            cancellationToken);
        var integrationEvent = new SubscriptionCustomRequestRejectedIntegrationEvent(
            Guid.NewGuid(),
            _clock.UtcNow,
            request.Id,
            request.OperatorId,
            request.RejectionReason!);
        await _outbox.EnqueueAsync(
            integrationEvent.EventId,
            SubscriptionCustomRequestRejectedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        return SubscriptionCustomRequestMapper.ToDto(request);
    }
}
