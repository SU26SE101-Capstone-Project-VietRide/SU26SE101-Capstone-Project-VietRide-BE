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

    Task AddAsync(ParcelCustodyExceptionRequest entity, CancellationToken ct = default);
}
