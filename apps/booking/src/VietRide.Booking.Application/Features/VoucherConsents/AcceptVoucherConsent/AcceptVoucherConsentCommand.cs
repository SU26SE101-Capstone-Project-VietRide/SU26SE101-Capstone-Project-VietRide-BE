using MediatR;

namespace VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;

/// <summary>
/// Command for POST /v1/operator/voucher-consents/{id}/accept.
/// <para>
/// Precondition: consent status = PENDING.
/// Emits <c>booking.voucher.consent_accepted</c> via Outbox.
/// </para>
/// </summary>
public sealed record AcceptVoucherConsentCommand(
    Guid ConsentId,
    Guid CallerOperatorId,
    Guid CallerUserId) : IRequest<AcceptVoucherConsentResult>;
