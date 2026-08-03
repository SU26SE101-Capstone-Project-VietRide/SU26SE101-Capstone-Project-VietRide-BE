using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class PlatformTripStatsBackfillJob
{
    public const string RecurringJobId = "trip.platform-stats-backfill";

    private readonly TripDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PlatformTripStatsBackfillJob> _logger;

    public PlatformTripStatsBackfillJob(
        TripDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<PlatformTripStatsBackfillJob> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT vietride_trip.rebuild_platform_trip_stats();",
            cancellationToken);
        await InvalidatePlatformReportCacheAsync();
    }

    private async Task InvalidatePlatformReportCacheAsync()
    {
        try
        {
            var database = _redis.GetDatabase();
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var keys = _redis.GetServer(endpoint)
                    .Keys(pattern: "platform-report:*", pageSize: 1000)
                    .ToArray();
                if (keys.Length > 0)
                {
                    await database.KeyDeleteAsync(keys);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Platform report cache invalidation failed after TripStats backfill.");
        }
    }
}
