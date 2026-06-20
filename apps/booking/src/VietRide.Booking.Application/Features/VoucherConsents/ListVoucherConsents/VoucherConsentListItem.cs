using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// A single item in the operator voucher-consent list (v7:677-679).
/// </summary>
public sealed record VoucherConsentListItem(
    Guid Id,
    Guid VoucherId,
    string VoucherCode,
    VoucherType VoucherType,
    long VoucherValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    long MinOrderAmount,
    long? MaxDiscountAmount,
    IReadOnlyList<Guid>? ApplicableRouteIds,
    OperatorVoucherConsentStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? RespondedAt,
    Guid? RespondedByUserId);
