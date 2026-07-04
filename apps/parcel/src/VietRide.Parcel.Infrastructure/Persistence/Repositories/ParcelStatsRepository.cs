using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Repositories;

internal sealed class ParcelStatsRepository : IParcelStatsRepository
{
    private readonly ParcelDbContext _db;

    public ParcelStatsRepository(ParcelDbContext db)
    {
        _db = db;
    }

    public async Task<ParcelStats?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.ParcelStats.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<ParcelStats> AddAsync(ParcelStats entity, CancellationToken ct)
    {
        await _db.ParcelStats.AddAsync(entity, ct);
        return entity;
    }

    public void Update(ParcelStats entity)
        => _db.ParcelStats.Update(entity);

    public void Remove(ParcelStats entity)
        => _db.ParcelStats.Remove(entity);

    public IQueryable<ParcelStats> Query()
        => _db.ParcelStats;

    public IQueryable<ParcelStats> QueryNoTracking()
        => _db.ParcelStats.AsNoTracking();

    public async Task UpsertIncrementAsync(
        Guid operatorId,
        DateOnly statDate,
        int totalParcels,
        int totalLoaded,
        int totalDelivered,
        int totalRejected,
        int totalReturned,
        long totalRevenue,
        long totalRefunded,
        CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcel_stats
                (id, operator_id, stat_date, total_parcels, total_loaded, total_delivered,
                 total_rejected, total_returned, total_revenue, total_refunded, updated_at)
            VALUES
                (gen_random_uuid(), {operatorId}, {statDate}, {totalParcels}, {totalLoaded}, {totalDelivered},
                 {totalRejected}, {totalReturned}, {totalRevenue}, {totalRefunded}, now())
            ON CONFLICT (operator_id, stat_date)
            DO UPDATE SET
                total_parcels = parcel_stats.total_parcels + EXCLUDED.total_parcels,
                total_loaded = parcel_stats.total_loaded + EXCLUDED.total_loaded,
                total_delivered = parcel_stats.total_delivered + EXCLUDED.total_delivered,
                total_rejected = parcel_stats.total_rejected + EXCLUDED.total_rejected,
                total_returned = parcel_stats.total_returned + EXCLUDED.total_returned,
                total_revenue = parcel_stats.total_revenue + EXCLUDED.total_revenue,
                total_refunded = parcel_stats.total_refunded + EXCLUDED.total_refunded,
                updated_at = now();
            """, ct);
    }
}
