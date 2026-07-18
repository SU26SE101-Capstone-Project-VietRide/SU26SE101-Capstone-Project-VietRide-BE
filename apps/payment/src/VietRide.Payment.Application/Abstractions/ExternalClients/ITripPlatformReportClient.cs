using VietRide.Payment.Application.Features.Admin.PlatformReports;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface ITripPlatformReportClient
{
    Task<IReadOnlyList<TripPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
