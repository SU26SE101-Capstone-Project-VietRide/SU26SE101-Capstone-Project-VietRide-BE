using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<TripEntity, Guid>
{
    Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken);
}
