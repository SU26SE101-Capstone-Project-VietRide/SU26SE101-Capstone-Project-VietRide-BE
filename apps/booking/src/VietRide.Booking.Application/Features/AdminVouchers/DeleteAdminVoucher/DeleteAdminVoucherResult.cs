namespace VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;

/// <summary>
/// Response payload for DELETE /v1/admin/vouchers/{id}.
/// </summary>
public sealed record DeleteAdminVoucherResult(
    Guid Id,
    DateTimeOffset DeletedAt);
