using MediatR;

namespace VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;

/// <summary>
/// Command for POST /v1/operator/voucher-consents/{id}/reject.
/// <para>
/// Precondition: consent status IN (PENDING, ACCEPTED).
/// Revoking an ACCEPTED consent (ACCEPTED → REJECTED) does NOT roll back discounts
/// on already-CONFIRMED bookings.
/// Emits <c>booking.voucher.consent_rejected</c> via Outbox.
/// </para>
/// </summary>
public sealed record RejectVoucherConsentCommand(
    Guid ConsentId,
    Guid CallerOperatorId,
    Guid CallerUserId,
    string? Reason) : IRequest<RejectVoucherConsentResult>;
