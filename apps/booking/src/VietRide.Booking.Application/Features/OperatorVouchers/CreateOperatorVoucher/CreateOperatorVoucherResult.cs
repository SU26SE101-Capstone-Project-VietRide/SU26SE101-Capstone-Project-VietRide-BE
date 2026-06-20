using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;

/// <summary>
/// Response payload for POST /v1/operator/vouchers (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record CreateOperatorVoucherResult(
    Guid Id,
    string Code,
    string Name,
    VoucherType Type,
    long Value,
    VoucherFundingType FundingType,
    /// <summary>Always the caller's operatorId for operator-created vouchers.</summary>
    Guid OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt);
