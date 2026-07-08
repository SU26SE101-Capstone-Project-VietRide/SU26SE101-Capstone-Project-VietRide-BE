using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class CampaignRepository : ICampaignRepository
{
    private readonly BookingDbContext _db;

    public CampaignRepository(BookingDbContext db)
    {
        _db = db;
    }

    public async Task<Campaign?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Campaign> AddAsync(Campaign entity, CancellationToken ct)
    {
        await _db.Campaigns.AddAsync(entity, ct);
        return entity;
    }

    public void Update(Campaign entity) => _db.Campaigns.Update(entity);

    public void Remove(Campaign entity) => _db.Campaigns.Remove(entity);

    public IQueryable<Campaign> Query() => _db.Campaigns;

    public IQueryable<Campaign> QueryNoTracking() => _db.Campaigns.AsNoTracking();

    public IQueryable<CampaignVoucher> QueryCampaignVouchersNoTracking()
        => _db.CampaignVouchers.AsNoTracking();

    public async Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken ct = default)
        => await _db.Campaigns.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task ReplaceVouchersAsync(Guid campaignId, IReadOnlyCollection<Guid> voucherIds, CancellationToken ct = default)
    {
        var existing = await _db.CampaignVouchers.Where(x => x.CampaignId == campaignId).ToListAsync(ct);
        _db.CampaignVouchers.RemoveRange(existing);

        foreach (var voucherId in voucherIds.Distinct())
        {
            await _db.CampaignVouchers.AddAsync(CampaignVoucher.Create(campaignId, voucherId), ct);
        }
    }
}
