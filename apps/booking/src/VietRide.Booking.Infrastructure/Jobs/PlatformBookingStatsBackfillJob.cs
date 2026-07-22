using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class PlatformBookingStatsBackfillJob
{
    public const string RecurringJobId = "booking.platform-stats-backfill";

    private readonly BookingDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PlatformBookingStatsBackfillJob> _logger;

    public PlatformBookingStatsBackfillJob(
        BookingDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<PlatformBookingStatsBackfillJob> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT vietride_booking.rebuild_platform_booking_stats();",
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
            _logger.LogWarning(exception, "Platform report cache invalidation failed after BookingStats backfill.");
        }
    }
}
