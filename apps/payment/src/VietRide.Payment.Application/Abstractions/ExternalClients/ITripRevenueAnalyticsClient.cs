using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface ITripRevenueAnalyticsClient
{
    Task<IReadOnlyList<TripVehicleCountItem>> GetVehicleCountsAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TripRoutePerformanceItem>> GetRoutePerformanceAsync(
        Guid operatorId,
        string month,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TripRevenueSummaryItem>> GetTripSummariesAsync(
        IReadOnlyList<Guid> tripIds,
        CancellationToken cancellationToken = default);
}
