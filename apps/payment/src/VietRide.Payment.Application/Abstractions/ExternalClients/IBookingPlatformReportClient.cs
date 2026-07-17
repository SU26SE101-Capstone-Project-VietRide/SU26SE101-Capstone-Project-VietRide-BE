using VietRide.Payment.Application.Features.Admin.PlatformReports;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface IBookingPlatformReportClient
{
    Task<IReadOnlyList<BookingPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
