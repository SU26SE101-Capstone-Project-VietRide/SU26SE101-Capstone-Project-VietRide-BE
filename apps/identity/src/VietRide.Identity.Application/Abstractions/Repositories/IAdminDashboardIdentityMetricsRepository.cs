namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IAdminDashboardIdentityMetricsRepository
{
    Task<AdminDashboardIdentityMetricsReadResult> GetAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default);
}
