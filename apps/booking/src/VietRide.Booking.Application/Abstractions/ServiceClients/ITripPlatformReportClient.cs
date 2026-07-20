using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface ITripPlatformReportClient
{
    Task<IReadOnlyList<TripPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);
}
