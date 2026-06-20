namespace VietRide.Booking.Domain.Enums;

/// <summary>
/// Status machine for <see cref="Entities.OperatorVoucherConsent"/> — an operator's opt-in
/// decision on an admin-created OPERATOR_FUNDED voucher that targets it.
/// <list type="bullet">
/// <item><term>PENDING</term><description>Awaiting operator response (initial state).</description></item>
/// <item><term>ACCEPTED</term><description>Operator opted in — voucher applies at checkout for that operator.</description></item>
/// <item><term>REJECTED</term><description>Operator opted out. Reachable from PENDING (reject) or
/// ACCEPTED (revoke). Revoking does NOT roll back discounts on already-CONFIRMED bookings.</description></item>
/// </list>
/// </summary>
public enum OperatorVoucherConsentStatus
{
    PENDING,
    ACCEPTED,
    REJECTED,
}
