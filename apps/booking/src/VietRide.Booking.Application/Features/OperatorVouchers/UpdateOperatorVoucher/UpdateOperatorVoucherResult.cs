using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;

/// <summary>
/// Response payload for PATCH /v1/operator/vouchers/{id} (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record UpdateOperatorVoucherResult(
    Guid Id,
    string Code,
    string Name,
    VoucherType Type,
    long Value,
    VoucherFundingType FundingType,
    Guid OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil);
