namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// A single item in the operator voucher-consent list (v7:677-679).
/// </summary>
public sealed record VoucherConsentListItem(
    Guid Id,
    Guid VoucherId,
    string VoucherCode,
    string VoucherType,
    long VoucherValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    long MinOrderAmount,
    long? MaxDiscountAmount,
    IReadOnlyList<Guid>? ApplicableRouteIds,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? RespondedAt,
    Guid? RespondedByUserId);
