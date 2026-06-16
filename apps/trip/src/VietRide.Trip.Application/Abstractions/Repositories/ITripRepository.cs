using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<Domain.Entities.Trip, Guid>
{
    Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken);
}
