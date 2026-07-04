using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

/// <summary>
/// Repository cho ParcelRouteFare có composite PK (RouteId, SizeCategory).
/// KHÔNG kế thừa IRepository&lt;TEntity, Guid&gt; vì Id bị Ignore trong EF config.
/// </summary>
public interface IParcelRouteFareRepository
{
    Task<ParcelRouteFare?> FindByCompositeAsync(Guid routeId, ParcelSizeCategory sizeCategory, CancellationToken ct = default);
    Task<ParcelRouteFare> AddAsync(ParcelRouteFare entity, CancellationToken ct);
    void Update(ParcelRouteFare entity);
    void Remove(ParcelRouteFare entity);
    IQueryable<ParcelRouteFare> Query();
    IQueryable<ParcelRouteFare> QueryNoTracking();
}
