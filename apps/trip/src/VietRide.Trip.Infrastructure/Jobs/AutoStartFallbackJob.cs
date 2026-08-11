using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class AutoStartFallbackJob
{
    private const string EventType = "trip.trip.started";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TripDbContext dbContext;
    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;
    private readonly IResourceAvailabilityService? resourceAvailability;
    private readonly ITripAssignmentAlertStore? assignmentAlerts;

    public AutoStartFallbackJob(
        TripDbContext dbContext,
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IClock clock,
        IResourceAvailabilityService? resourceAvailability = null,
        ITripAssignmentAlertStore? assignmentAlerts = null)
    {
        this.dbContext = dbContext;
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.clock = clock;
        this.resourceAvailability = resourceAvailability;
        this.assignmentAlerts = assignmentAlerts;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(300)]
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tripIds = await tripRepository.ListBoardingForAutoStartAsync(
            now.AddMinutes(-30), cancellationToken);
        foreach (var tripId in tripIds)
        {
            await ProcessAsync(tripId, now, cancellationToken);
        }
    }

    private async Task ProcessAsync(Guid tripId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var trip = await tripRepository.AcquireForLifecycleTransitionAsync(tripId, cancellationToken);
        if (trip is null
            || trip.Status != TripStatus.BOARDING
            || trip.DepartureDateTime >= now.AddMinutes(-30))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        try
        {
            if (resourceAvailability is not null)
            {
                await resourceAvailability.ActivateTripAsync(trip.Id, now, cancellationToken);
            }
            trip.Start(now);
        }
        catch (CodedConflictException exception) when (
            assignmentAlerts is not null
            && GetError(exception, "conflictReason") == "RESOURCE_ACTIVE")
        {
            await transaction.RollbackAsync(cancellationToken);
            await PersistStartBlockedAlertAsync(tripId, exception, now, cancellationToken);
            return;
        }
        await outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(new { tripId = trip.Id, actualDepartureTime = now }, JsonOptions),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PersistStartBlockedAlertAsync(
        Guid tripId,
        CodedConflictException exception,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var trip = await tripRepository.AcquireForLifecycleTransitionAsync(tripId, cancellationToken);
        if (trip is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        if (await assignmentAlerts!.TryAddStartBlockedAsync(trip.Id, trip.OperatorId, cancellationToken))
        {
            var integrationEvent = new TripAssignmentStartBlockedIntegrationEvent(
                Guid.NewGuid(),
                occurredAt,
                trip.Id,
                trip.OperatorId,
                GetError(exception, "resourceRole") ?? "DRIVER",
                ParseGuidError(exception, "resourceId"),
                GetError(exception, "conflictingSourceType") ?? "TRIP",
                ParseGuidError(exception, "conflictingSourceId"),
                "RESOURCE_ACTIVE",
                ParseDateTimeOffsetError(exception, "blockingUntil"));
            await outbox.EnqueueAsync(
                integrationEvent.EventId,
                integrationEvent.EventType,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string? GetError(CodedConflictException exception, string field) =>
        exception.Errors.FirstOrDefault(error => string.Equals(error.Field, field, StringComparison.Ordinal))?.Message;

    private static Guid ParseGuidError(CodedConflictException exception, string field) =>
        Guid.TryParse(GetError(exception, field), out var value) ? value : Guid.Empty;

    private static DateTimeOffset? ParseDateTimeOffsetError(CodedConflictException exception, string field) =>
        DateTimeOffset.TryParse(GetError(exception, field), out var value) ? value : null;
}
