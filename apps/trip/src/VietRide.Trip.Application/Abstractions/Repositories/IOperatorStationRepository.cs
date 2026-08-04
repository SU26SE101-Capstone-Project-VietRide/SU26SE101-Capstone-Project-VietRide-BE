using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorStationRepository : IRepository<OperatorStation, Guid>
{
    Task<bool> ExistsActiveAsync(
        Guid operatorId,
        Guid stationId,
        CancellationToken cancellationToken)
        => Task.FromResult(QueryNoTracking().Any(item =>
            item.OperatorId == operatorId
            && item.StationId == stationId
            && item.IsActive));

    Task<OperatorStation?> AcquireActiveForRouteProposalApprovalAsync(
        Guid operatorId,
        Guid stationId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-proposal operator-station locking is not implemented by this repository.");

    Task<(int RelinkedCount, int CollapsedCount)> RelinkForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator-station merge relinking is not implemented by this repository.");
}
