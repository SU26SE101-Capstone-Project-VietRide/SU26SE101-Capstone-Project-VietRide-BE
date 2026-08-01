namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorAnalyticsRepository
{
    Task<IReadOnlyList<OperatorVehicleCountReadModel>> GetVehicleCountsAsync(
        IReadOnlyCollection<Guid> operatorIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OperatorRoutePerformanceReadModel>> GetRoutePerformanceAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
