namespace VietRide.Booking.Domain.Enums;

/// <summary>
/// Discount calculation strategy for a <see cref="Entities.Voucher"/>.
/// <list type="bullet">
/// <item><term>PERCENT_OFF</term><description>value is a percentage (1–100) of the order amount.</description></item>
/// <item><term>FIXED_AMOUNT</term><description>value is a flat VND amount subtracted from the order.</description></item>
/// </list>
/// </summary>
public enum VoucherType
{
    PERCENT_OFF,
    FIXED_AMOUNT,
}
