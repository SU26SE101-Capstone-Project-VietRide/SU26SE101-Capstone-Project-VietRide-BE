namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IIdentityDashboardMetricsClient
{
    Task<IdentityDashboardMetricsDto> GetAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
