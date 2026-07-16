using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripVehicleSwapService
{
    Task<bool> StageSwapAsync(
        Domain.Entities.Trip trip,
        Vehicle oldVehicle,
        Vehicle newVehicle,
        IReadOnlyCollection<TripSeat> lockedSeats,
        IReadOnlyCollection<VehicleSwapBookingSeatImpact> bookingSeatImpacts,
        Guid actorUserId,
        string auditAction,
        string requestId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
