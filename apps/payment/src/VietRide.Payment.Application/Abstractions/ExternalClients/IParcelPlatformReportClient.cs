using VietRide.Payment.Application.Features.Admin.PlatformReports;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface IParcelPlatformReportClient
{
    Task<IReadOnlyList<ParcelPlatformReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
