namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IOperatorParcelStatsRepository
{
    Task<OperatorParcelStatsReadResult> GetAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string groupBy,
        int routeLimit,
        CancellationToken cancellationToken = default);
}
