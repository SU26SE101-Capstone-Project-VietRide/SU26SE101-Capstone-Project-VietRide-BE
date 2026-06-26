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
}
