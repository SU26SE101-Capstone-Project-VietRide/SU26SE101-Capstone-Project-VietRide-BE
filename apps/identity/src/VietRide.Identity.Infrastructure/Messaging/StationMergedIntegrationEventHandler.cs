using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class StationMergedIntegrationEventHandler
    : IIntegrationEventHandler<StationMergedIntegrationEvent>
{
    private readonly IActivityLogRepository _activityLogs;
    private readonly ILogger<StationMergedIntegrationEventHandler> _logger;

    public StationMergedIntegrationEventHandler(
        IActivityLogRepository activityLogs,
        ILogger<StationMergedIntegrationEventHandler> logger)
    {
        _activityLogs = activityLogs;
        _logger = logger;
    }

    public async Task HandleAsync(
        StationMergedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        StationAuditEventValidation.Validate(integrationEvent);
        if (await _activityLogs.ExistsBySourceEventIdAsync(
            integrationEvent.EventId,
            cancellationToken))
        {
            _logger.LogDebug(
                "Station audit event {EventId} was already persisted.",
                integrationEvent.EventId);
            return;
        }

        await _activityLogs.AddAsync(
            ActivityLog.Create(
                integrationEvent.ActorUserId,
                ActivityLogAction.STATION_MERGED,
                metadata: null,
                ipAddress: integrationEvent.IpAddress,
                userAgent: integrationEvent.UserAgent,
                sourceEventId: integrationEvent.EventId),
            cancellationToken);
        _logger.LogInformation(
            "Station audit event {EventId} queued as {Action}.",
            integrationEvent.EventId,
            ActivityLogAction.STATION_MERGED);
    }
}
