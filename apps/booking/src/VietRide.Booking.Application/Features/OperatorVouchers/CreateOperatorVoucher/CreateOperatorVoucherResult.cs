namespace VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;

/// <summary>
/// Response payload for POST /v1/operator/vouchers (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record CreateOperatorVoucherResult(
    Guid Id,
    string Code,
    string Name,
    string Type,
    long Value,
    string FundingType,
    /// <summary>Always the caller's operatorId for operator-created vouchers.</summary>
    Guid OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt);
