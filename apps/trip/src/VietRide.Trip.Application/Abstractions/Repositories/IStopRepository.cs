using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IStopRepository : IRepository<Stop, Guid>
{
    Task<IReadOnlyList<Stop>> AcquireForRouteProposalApprovalAsync(
        IReadOnlyCollection<Guid> stopIds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-proposal approval locking is not implemented by this repository.");
}
