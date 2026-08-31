using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelStopDepartureApprovalRepository
{
    Task AcquireTripStopLockAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default);

    Task<ParcelStopDepartureApprovalRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken ct = default);

    Task<ParcelStopDepartureApprovalRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken ct = default);

    Task<ParcelStopDepartureApprovalRequest?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default);

    Task<ParcelStopDepartureApprovalRequest?> GetLatestByTripStopAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default);

    Task<ParcelStopDepartureApprovalRequest?> GetLatestByTripStopForUpdateAsync(
        Guid tripId,
        Guid stopId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelStopDepartureApprovalRequest>> ListPendingByOperatorAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParcelStopDepartureApprovalRequest>>([]);

    Task<IReadOnlyList<ParcelStopDepartureApprovalRequest>> ListPendingByTripForUpdateAsync(
        Guid tripId,
        Guid? stopId = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParcelStopDepartureApprovalRequest>>([]);

    Task AddAsync(ParcelStopDepartureApprovalRequest entity, CancellationToken ct = default);
}
