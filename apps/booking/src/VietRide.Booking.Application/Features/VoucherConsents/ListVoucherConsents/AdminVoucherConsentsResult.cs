namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Response payload for GET /v1/admin/vouchers/{voucherId}/consents (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record AdminVoucherConsentsResult(
    Guid VoucherId,
    IReadOnlyList<AdminVoucherConsentItem> Items);
