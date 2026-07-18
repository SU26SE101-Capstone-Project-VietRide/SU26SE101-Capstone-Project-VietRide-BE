using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class StationNormalizedIntegrationEventHandler
    : IIntegrationEventHandler<StationNormalizedIntegrationEvent>
{
    private const string SourceEventConstraint = "uq_activity_logs_source_event_id";
    private readonly IActivityLogRepository _activityLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StationNormalizedIntegrationEventHandler> _logger;

    public StationNormalizedIntegrationEventHandler(
        IActivityLogRepository activityLogs,
        IUnitOfWork unitOfWork,
        ILogger<StationNormalizedIntegrationEventHandler> logger)
    {
        _activityLogs = activityLogs;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        StationNormalizedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        StationAuditEventValidation.Validate(integrationEvent);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _activityLogs.ExistsBySourceEventIdAsync(
                integrationEvent.EventId,
                cancellationToken))
            {
                await _unitOfWork.CommitAsync(cancellationToken);
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
            await _unitOfWork.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Station audit event {EventId} persisted as {Action}.",
                integrationEvent.EventId,
                ActivityLogAction.STATION_NORMALIZED);
        }
        catch (DbUpdateException exception) when (IsSourceEventReplay(exception))
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            _logger.LogDebug(
                "Concurrent replay of Station audit event {EventId} was ignored.",
                integrationEvent.EventId);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsSourceEventReplay(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SourceEventConstraint,
        };
}
