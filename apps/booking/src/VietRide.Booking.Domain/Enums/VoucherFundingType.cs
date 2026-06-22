namespace VietRide.Booking.Domain.Enums;

/// <summary>
/// Who bears the discount cost for a <see cref="Entities.Voucher"/>.
/// <list type="bullet">
/// <item><term>VIETRIDE_FUNDED</term><description>Platform bears the discount (admin voucher only).</description></item>
/// <item><term>OPERATOR_FUNDED</term><description>The owning/targeted operator bears the discount.
/// Admin OPERATOR_FUNDED vouchers require per-operator consent; operator self-created vouchers are
/// always OPERATOR_FUNDED and self-consented (no consent fan-out).</description></item>
/// </list>
/// </summary>
public enum VoucherFundingType
{
    VIETRIDE_FUNDED,
    OPERATOR_FUNDED,
}
