using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface ICampaignRepository : IRepository<Campaign, Guid>
{
    Task<IReadOnlyList<Campaign>> ListAsync(CancellationToken ct = default);

    IQueryable<CampaignVoucher> QueryCampaignVouchersNoTracking();

    Task ReplaceVouchersAsync(Guid campaignId, IReadOnlyCollection<Guid> voucherIds, CancellationToken ct = default);
}
