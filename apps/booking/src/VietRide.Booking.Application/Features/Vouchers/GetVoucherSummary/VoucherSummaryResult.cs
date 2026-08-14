namespace VietRide.Booking.Application.Features.Vouchers.GetVoucherSummary;

public sealed record VoucherSummaryResult(
    int Total,
    int Active,
    int Booking,
    int Parcel,
    int ExpiringIn7Days);
