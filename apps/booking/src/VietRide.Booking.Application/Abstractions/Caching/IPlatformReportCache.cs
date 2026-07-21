using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.Caching;

public interface IPlatformReportCache
{
    Task<PlatformReportResult?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, PlatformReportResult value, TimeSpan ttl, CancellationToken ct = default);
}
