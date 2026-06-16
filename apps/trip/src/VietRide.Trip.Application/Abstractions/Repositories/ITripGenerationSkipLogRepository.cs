using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripGenerationSkipLogRepository : IRepository<TripGenerationSkipLog, Guid>
{
}
