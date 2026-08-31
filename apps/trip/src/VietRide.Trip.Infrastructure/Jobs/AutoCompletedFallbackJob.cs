using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class AutoCompletedFallbackJob
{
    private const string EventType = "trip.trip.completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TripDbContext dbContext;
    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IRouteChangeProposalService? routeChangeProposals;
    private readonly IResourceAvailabilityService? resourceAvailability;

    public AutoCompletedFallbackJob(
        TripDbContext dbContext,
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ITripAuditLogRepository auditLogs,
        IRouteChangeProposalService? routeChangeProposals = null,
        IResourceAvailabilityService? resourceAvailability = null)
    {
        this.dbContext = dbContext;
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.clock = clock;
        this.auditLogs = auditLogs;
        this.routeChangeProposals = routeChangeProposals;
        this.resourceAvailability = resourceAvailability;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(900)]
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tripIds = await tripRepository.ListInProgressForAutoCompletionAsync(
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
            || trip.Status != TripStatus.IN_PROGRESS
            || trip.EstimatedArrivalTime >= now.AddMinutes(-30))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        trip.CompleteAutomatically(now);
        if (resourceAvailability is not null)
        {
            await resourceAvailability.ReleaseTripAsync(trip.Id, now, cancellationToken);
        }
        if (routeChangeProposals is not null)
            await routeChangeProposals.ExpirePendingForTripAsync(trip.Id, now, cancellationToken);
        if (trip.DestinationArrivedAt is null)
        {
            await auditLogs.AddAsync(
                TripAuditLog.Create(
                    Guid.NewGuid(),
                    trip.Id,
                    null,
                    TripAuditAction.TripCompletedFallbackWithoutDestinationArrival,
                    JsonSerializer.Serialize(new
                    {
                        tripId = trip.Id,
                        estimatedArrivalTime = trip.EstimatedArrivalTime,
                        destinationArrivedAt = (DateTimeOffset?)null,
                        reason = "DESTINATION_ARRIVAL_NOT_RECORDED",
                    }, JsonOptions),
                    now),
                cancellationToken);
        }
        var integrationEvent = new TripCompletedIntegrationEvent(
            trip.Id,
            trip.OperatorId,
            now,
            trip.HasSubstitution,
            trip.TripCode,
            trip.Source.ToString());
        await outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
