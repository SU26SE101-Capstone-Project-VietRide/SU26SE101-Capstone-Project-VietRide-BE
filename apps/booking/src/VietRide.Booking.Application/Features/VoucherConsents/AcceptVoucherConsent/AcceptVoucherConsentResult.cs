namespace VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;

/// <summary>
/// Response payload for POST /v1/operator/voucher-consents/{id}/accept (ADR 0004 ApiResponse envelope).
/// Shape aligns to API contract: { id, status }.
/// </summary>
public sealed record AcceptVoucherConsentResult(
    Guid Id,
    string Status);
