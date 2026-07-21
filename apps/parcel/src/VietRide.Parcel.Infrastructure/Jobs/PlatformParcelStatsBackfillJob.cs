using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class PlatformParcelStatsBackfillJob
{
    public const string RecurringJobId = "parcel.platform-stats-backfill";

    private readonly ParcelDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PlatformParcelStatsBackfillJob> _logger;

    public PlatformParcelStatsBackfillJob(
        ParcelDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<PlatformParcelStatsBackfillJob> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT vietride_parcel.rebuild_platform_parcel_stats();",
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
                    .Keys(pattern: "platform-report:v1:*", pageSize: 1000)
                    .ToArray();
                if (keys.Length > 0)
                {
                    await database.KeyDeleteAsync(keys);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Platform report cache invalidation failed after ParcelStats backfill.");
        }
    }
}
