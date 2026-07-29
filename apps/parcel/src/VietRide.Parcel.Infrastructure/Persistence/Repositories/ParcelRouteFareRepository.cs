using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelRouteFareRepository : IParcelRouteFareRepository
{
    private readonly ParcelDbContext _db;

    public ParcelRouteFareRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public async Task<ParcelRouteFare?> FindByCompositeAsync(Guid routeId, ParcelSizeCategory sizeCategory, CancellationToken ct = default)
        => await _db.ParcelRouteFares.FirstOrDefaultAsync(
            f => f.RouteId == routeId && f.SizeCategory == sizeCategory, ct);

    public async Task AcquireRouteBatchLockAsync(Guid routeId, CancellationToken ct = default)
    {
        var lockKey = BitConverter.ToInt64(routeId.ToByteArray(), 0);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey});",
            ct);
    }

    public async Task<IReadOnlyList<ParcelRouteFare>> FindByRouteAndSizesAsync(
        Guid routeId,
        IReadOnlyCollection<ParcelSizeCategory> sizeCategories,
        CancellationToken ct = default)
    {
        var categories = sizeCategories.ToArray();
        return await _db.ParcelRouteFares
            .Where(fare => fare.RouteId == routeId
                && categories.Contains(fare.SizeCategory))
            .ToListAsync(ct);
    }

    public async Task<ParcelRouteFare> AddAsync(ParcelRouteFare entity, CancellationToken ct)
    {
        await _db.ParcelRouteFares.AddAsync(entity, ct);
        return entity;
    }

    public async Task AddRangeAsync(IReadOnlyCollection<ParcelRouteFare> entities, CancellationToken ct)
        => await _db.ParcelRouteFares.AddRangeAsync(entities, ct);

    public void Update(ParcelRouteFare entity)
        => _db.ParcelRouteFares.Update(entity);

    public void Remove(ParcelRouteFare entity)
        => _db.ParcelRouteFares.Remove(entity);

    public IQueryable<ParcelRouteFare> Query()
        => _db.ParcelRouteFares;

    public IQueryable<ParcelRouteFare> QueryNoTracking()
        => _db.ParcelRouteFares.AsNoTracking();
}
