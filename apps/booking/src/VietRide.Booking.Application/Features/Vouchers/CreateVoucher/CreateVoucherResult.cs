using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Vouchers.CreateVoucher;

/// <summary>
/// Response payload for POST /v1/admin/vouchers (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record CreateVoucherResult(
    Guid Id,
    string Code,
    string Name,
    VoucherType Type,
    long Value,
    VoucherFundingType FundingType,
    /// <summary>Always null for admin-created vouchers.</summary>
    Guid? OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt);
