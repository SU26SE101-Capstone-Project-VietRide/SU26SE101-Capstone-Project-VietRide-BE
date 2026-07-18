using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IOperatorStationRepository : IRepository<OperatorStation, Guid>
{
    Task<(int RelinkedCount, int CollapsedCount)> RelinkForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator-station merge relinking is not implemented by this repository.");
}
