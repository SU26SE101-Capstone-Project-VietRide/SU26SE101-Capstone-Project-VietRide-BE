using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class StationNormalizedIntegrationEventHandler
    : IIntegrationEventHandler<StationNormalizedIntegrationEvent>
{
    private readonly IActivityLogRepository _activityLogs;
    private readonly ILogger<StationNormalizedIntegrationEventHandler> _logger;

    public StationNormalizedIntegrationEventHandler(
        IActivityLogRepository activityLogs,
        ILogger<StationNormalizedIntegrationEventHandler> logger)
    {
        _activityLogs = activityLogs;
        _logger = logger;
    }

    public async Task HandleAsync(
        StationNormalizedIntegrationEvent integrationEvent,
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
                ActivityLogAction.STATION_NORMALIZED,
                metadata: null,
                ipAddress: integrationEvent.IpAddress,
                userAgent: integrationEvent.UserAgent,
                sourceEventId: integrationEvent.EventId),
            cancellationToken);
        _logger.LogInformation(
            "Station audit event {EventId} queued as {Action}.",
            integrationEvent.EventId,
            ActivityLogAction.STATION_NORMALIZED);
    }
}
