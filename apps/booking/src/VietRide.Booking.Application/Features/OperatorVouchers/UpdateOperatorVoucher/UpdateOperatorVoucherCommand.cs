using MediatR;

namespace VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;

/// <summary>
/// Command for PATCH /v1/operator/vouchers/{id} — partial update of a voucher scoped to the caller's operator.
/// <para>
/// All mutable fields are nullable: <c>null</c> means "keep the current value". Immutable fields
/// (code, type, fundingType, ownerOperatorId) are not exposed here.
/// Freeze-on-first-use (Q6 RESOLVED): while <c>CountUsages == 0</c> all fields below are mutable.
/// Once <c>CountUsages &gt;= 1</c> the economic fields (<see cref="Value"/>, <see cref="MinOrderAmount"/>,
/// <see cref="MaxDiscountAmount"/>) are frozen — providing a non-null value that differs from the
/// current value returns 409 VOUCHER_LOCKED; omitting them (null) passes through without violation.
/// </para>
/// </summary>
public sealed record UpdateOperatorVoucherCommand(
    Guid VoucherId,
    /// <summary>Caller's operatorId from JWT — used for tenant-isolation check.</summary>
    Guid CallerOperatorId,
    /// <summary>New name. Null = keep current.</summary>
    string? Name,
    /// <summary>New discount value (VND). Null = keep current.</summary>
    long? Value,
    /// <summary>New minimum order amount (VND). Null = keep current.</summary>
    long? MinOrderAmount,
    /// <summary>New maximum discount cap (VND). Null = keep current (not "remove cap").</summary>
    long? MaxDiscountAmount,
    int? TotalUsageLimit,
    int? PerUserLimit,
    /// <summary>New valid-from timestamp. Null = keep current.</summary>
    DateTimeOffset? ValidFrom,
    /// <summary>New valid-until timestamp. Null = keep current.</summary>
    DateTimeOffset? ValidUntil,
    IReadOnlyList<Guid>? ApplicableRouteIds) : IRequest<UpdateOperatorVoucherResult>;
