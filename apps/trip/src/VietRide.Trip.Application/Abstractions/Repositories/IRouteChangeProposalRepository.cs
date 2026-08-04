using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IRouteChangeProposalRepository : IRepository<RouteChangeProposal, Guid>
{
    Task AcquireSourceCoordinationLockAsync(
        Guid sourceAlternativeRouteId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-change proposal source coordination is not implemented by this repository.");

    Task<RouteChangeProposal?> GetOwnedByIdAsync(Guid operatorId, Guid proposalId, CancellationToken cancellationToken);
    IQueryable<RouteChangeProposal> QueryWithStopsNoTracking();
    Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingByTripAsync(Guid tripId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingBySourceAsync(Guid sourceAlternativeRouteId, CancellationToken cancellationToken);
    Task LoadStopsAsync(RouteChangeProposal proposal, CancellationToken cancellationToken);
}
