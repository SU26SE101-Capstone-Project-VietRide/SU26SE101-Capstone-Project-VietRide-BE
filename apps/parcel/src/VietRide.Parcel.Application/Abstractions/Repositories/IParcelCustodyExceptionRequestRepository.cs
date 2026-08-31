using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelCustodyExceptionRequestRepository
{
    Task<ParcelCustodyExceptionRequest?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken ct = default);

    Task<ParcelCustodyExceptionRequest?> GetLatestByParcelAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<ParcelCustodyExceptionRequest?> GetLatestByParcelForUpdateAsync(
        Guid parcelId,
        CancellationToken ct = default);

    Task<ParcelCustodyExceptionRequest?> GetByIncidentAsync(
        Guid incidentId,
        CancellationToken ct = default);

    Task<ParcelCustodyExceptionRequest?> GetByIncidentForUpdateAsync(
        Guid incidentId,
        CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> ListPendingIncidentIdsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCustodyExceptionRequest>> ListLatestByParcelsAsync(
        IReadOnlyCollection<Guid> parcelIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<ParcelCustodyExceptionRequest>> ListPendingByOperatorAsync(
        Guid operatorId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParcelCustodyExceptionRequest>>([]);

    Task<IReadOnlyList<ParcelCustodyExceptionRequest>> ListPendingByTripForUpdateAsync(
        Guid tripId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParcelCustodyExceptionRequest>>([]);

    Task AddAsync(ParcelCustodyExceptionRequest entity, CancellationToken ct = default);
}
