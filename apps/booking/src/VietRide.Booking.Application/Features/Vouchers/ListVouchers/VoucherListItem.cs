using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Single row in the admin oversight voucher list (GET /v1/admin/vouchers, Q7).
/// </summary>
public sealed record VoucherListItem(
    Guid Id,
    string Code,
    string Name,
    VoucherType Type,
    long Value,
    VoucherFundingType FundingType,
    Guid? OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt);
