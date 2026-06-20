namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Response payload for GET /v1/operator/voucher-consents (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record ListVoucherConsentsResult(
    IReadOnlyList<VoucherConsentListItem> Items);
