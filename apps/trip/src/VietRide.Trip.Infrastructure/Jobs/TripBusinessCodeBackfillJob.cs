using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class TripBusinessCodeBackfillJob
{
    public const string RecurringJobId = "trip.business-code-backfill";
    public const int BatchSize = 100;

    private readonly TripDbContext db;
    private readonly ILogger<TripBusinessCodeBackfillJob> logger;

    public TripBusinessCodeBackfillJob(
        TripDbContext db,
        ILogger<TripBusinessCodeBackfillJob> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var trips = await db.Trips
            .Where(trip => trip.TripCode == null)
            .OrderBy(trip => trip.CreatedAt)
            .ThenBy(trip => trip.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        if (trips.Length == 0)
        {
            return;
        }

        foreach (var trip in trips)
        {
            trip.BackfillTripCode();
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Backfilled business codes for {TripCount} Trips.", trips.Length);
    }
}
