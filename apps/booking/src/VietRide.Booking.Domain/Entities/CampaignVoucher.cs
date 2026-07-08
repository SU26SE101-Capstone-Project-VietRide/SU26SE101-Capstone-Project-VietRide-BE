using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

public sealed class CampaignVoucher : BaseEntity<Guid>
{
    public Guid CampaignId { get; private set; }
    public Guid VoucherId { get; private set; }

    public Campaign? Campaign { get; private set; }
    public Voucher? Voucher { get; private set; }

    private CampaignVoucher() { }

    public static CampaignVoucher Create(Guid campaignId, Guid voucherId)
    {
        if (campaignId == Guid.Empty)
            throw new ArgumentException("Campaign id cannot be empty.", nameof(campaignId));
        if (voucherId == Guid.Empty)
            throw new ArgumentException("Voucher id cannot be empty.", nameof(voucherId));

        return new CampaignVoucher
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            VoucherId = voucherId,
        };
    }
}
