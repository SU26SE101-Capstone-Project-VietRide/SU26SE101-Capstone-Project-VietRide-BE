using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class AutoBoardingJob
{
    private const string EventType = "trip.trip.boarding_started";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TripDbContext dbContext;
    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;

    public AutoBoardingJob(
        TripDbContext dbContext,
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        this.dbContext = dbContext;
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.clock = clock;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(900)]
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tripIds = await tripRepository.ListScheduledForAutoBoardingAsync(
            now.AddMinutes(30), cancellationToken);
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
            || trip.Status != TripStatus.SCHEDULED
            || trip.DepartureDateTime > now.AddMinutes(30))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        trip.MarkBoarding(now);
        await outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(new { tripId = trip.Id, boardingStartedAt = now }, JsonOptions),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
