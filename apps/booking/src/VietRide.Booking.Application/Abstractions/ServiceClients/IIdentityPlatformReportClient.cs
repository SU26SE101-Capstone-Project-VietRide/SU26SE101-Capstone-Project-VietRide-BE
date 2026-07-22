using VietRide.Booking.Application.Features.Admin.PlatformReports;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IIdentityPlatformReportClient
{
    Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken ct = default);
}
