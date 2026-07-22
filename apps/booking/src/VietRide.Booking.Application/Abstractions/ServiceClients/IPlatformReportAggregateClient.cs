using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IPlatformReportAggregateClient
{
    Task<PlatformReportResult> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);
}
