using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Promotions;

public sealed class GetPromotionsQueryHandler : IRequestHandler<GetPromotionsQuery, IReadOnlyList<PromotionItem>>
{
    private readonly ICampaignRepository _campaigns;
    private readonly IClock _clock;

    public GetPromotionsQueryHandler(ICampaignRepository campaigns, IClock clock)
    {
        _campaigns = campaigns;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PromotionItem>> Handle(GetPromotionsQuery request, CancellationToken cancellationToken)
    {
        var service = request.Service.Trim().ToUpperInvariant();
        var now = _clock.UtcNow;

        return await _campaigns.QueryCampaignVouchersNoTracking()
            .Where(cv => cv.Campaign != null
                && cv.Campaign.IsActive
                && cv.Campaign.DeletedAt == null
                && cv.Campaign.ValidFrom <= now
                && cv.Campaign.ValidUntil >= now
                && cv.Voucher != null
                && cv.Voucher.IsActive
                && cv.Voucher.DeletedAt == null
                && cv.Voucher.ValidFrom <= now
                && cv.Voucher.ValidUntil >= now
                && cv.Voucher.ApplicableServices.Contains(service))
            .OrderBy(cv => cv.Campaign!.ValidUntil)
            .ThenBy(cv => cv.Voucher!.ValidUntil)
            .Take(20)
            .Select(cv => new PromotionItem(
                cv.Voucher!.Id,
                cv.Voucher.Code,
                cv.Voucher.Name,
                cv.Voucher.Type.ToString(),
                cv.Voucher.Value,
                cv.Voucher.ApplicableServices,
                cv.Voucher.ValidUntil))
            .ToListAsync(cancellationToken);
    }
}
