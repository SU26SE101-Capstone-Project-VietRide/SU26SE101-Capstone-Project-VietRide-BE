using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripAuditLogRepository
{
    Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);
}
