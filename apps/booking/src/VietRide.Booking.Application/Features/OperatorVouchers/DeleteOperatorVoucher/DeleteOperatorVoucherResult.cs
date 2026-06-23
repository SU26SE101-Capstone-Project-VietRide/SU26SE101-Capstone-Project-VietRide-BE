namespace VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;

/// <summary>
/// Response payload for DELETE /v1/operator/vouchers/{id} (ADR 0004 ApiResponse envelope).
/// Returns the voucher id and the timestamp at which it was soft-deleted.
/// </summary>
public sealed record DeleteOperatorVoucherResult(
    Guid Id,
    DateTimeOffset DeletedAt);
