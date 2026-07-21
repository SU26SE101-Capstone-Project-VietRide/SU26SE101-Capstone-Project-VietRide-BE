using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IPaymentPlatformLedgerClient
{
    Task<IReadOnlyList<PlatformLedgerReportItem>> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default);
}
