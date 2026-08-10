using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IStopRepository : IRepository<Stop, Guid>
{
    IQueryable<Stop> SearchByTextNoTracking(string search)
    {
        var normalized = search.Trim().ToLowerInvariant();
        return QueryNoTracking().Where(stop => stop.Name.ToLower().Contains(normalized)
            || (stop.Address != null && stop.Address.ToLower().Contains(normalized)));
    }

    Task<IReadOnlyList<Stop>> AcquireForRouteProposalApprovalAsync(
        IReadOnlyCollection<Guid> stopIds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-proposal approval locking is not implemented by this repository.");
}
