using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.SeatLock;

namespace VietRide.Trip.Infrastructure.SeatLock;

public sealed class SeatLockTtlProvider : ISeatLockTtlProvider
{
    private const int DefaultTtlMinutes = 10;

    public SeatLockTtlProvider(IConfiguration configuration)
    {
        var configuredTtl = configuration["SeatLock:TtlMinutes"]
            ?? Environment.GetEnvironmentVariable("SEAT_LOCK_TTL_MINUTES");
        var ttlMinutes = int.TryParse(configuredTtl, out var parsedTtlMinutes)
            ? parsedTtlMinutes
            : DefaultTtlMinutes;
        if (ttlMinutes <= 0)
        {
            ttlMinutes = DefaultTtlMinutes;
        }

        DefaultTtl = TimeSpan.FromMinutes(ttlMinutes);
    }

    public TimeSpan DefaultTtl { get; }
}
