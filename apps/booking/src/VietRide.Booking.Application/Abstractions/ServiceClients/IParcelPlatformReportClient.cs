using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IParcelPlatformReportClient
{
    Task<IReadOnlyList<ParcelPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);
}
