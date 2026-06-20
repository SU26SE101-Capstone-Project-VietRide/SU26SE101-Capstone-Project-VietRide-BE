using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// A single consent record in the admin voucher-consent view (v7:702-705).
/// </summary>
public sealed record AdminVoucherConsentItem(
    Guid Id,
    Guid OperatorId,
    Guid VoucherId,
    OperatorVoucherConsentStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? RespondedAt,
    Guid? RespondedByUserId,
    string? RejectReason);
