namespace VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;

/// <summary>
/// Response payload for POST /v1/operator/voucher-consents/{id}/reject (ADR 0004 ApiResponse envelope).
/// Shape aligns to API contract: { id, status }.
/// </summary>
public sealed record RejectVoucherConsentResult(
    Guid Id,
    string Status);
