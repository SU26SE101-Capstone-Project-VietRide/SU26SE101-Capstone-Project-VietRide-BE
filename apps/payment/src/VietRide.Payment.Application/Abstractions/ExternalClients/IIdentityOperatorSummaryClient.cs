using VietRide.Payment.Application.Features.Admin.PlatformReports;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface IIdentityOperatorSummaryClient
{
    Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default);
}
