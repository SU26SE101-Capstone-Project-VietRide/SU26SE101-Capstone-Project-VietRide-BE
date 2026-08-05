namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Request body for PATCH /v1/operator/vouchers/{id} (partial update — all fields optional).
/// <para>
/// code, type, fundingType, ownerOperatorId are ALWAYS immutable — they are NOT in this DTO.
/// Omitting a field (null) means "keep the current value". Freeze-on-first-use (Q6): once the
/// voucher has &gt;= 1 usage, the economic fields (<c>value</c>, <c>minOrderAmount</c>,
/// <c>maxDiscountAmount</c>) and <c>validFrom</c> are frozen. <c>validUntil</c> may only be
/// extended and usage limits may only be loosened; invalid locked edits return 409 VOUCHER_LOCKED.
/// Omitting a field passes through without triggering the freeze guard.
/// </para>
/// </summary>
public sealed class UpdateOperatorVoucherRequest
{
    public string? Name { get; init; }

    /// <summary>
    /// New discount value: percentage points for PERCENT_OFF, or VND for FIXED_AMOUNT.
    /// Null = keep current value.
    /// </summary>
    public long? Value { get; init; }

    /// <summary>New minimum order amount (VND). Null = keep current value.</summary>
    public long? MinOrderAmount { get; init; }

    /// <summary>New maximum discount cap (VND). Null = keep current value (not "remove cap").</summary>
    public long? MaxDiscountAmount { get; init; }

    public int? TotalUsageLimit { get; init; }

    public int? PerUserLimit { get; init; }

    /// <summary>
    /// New valid-from timestamp. Null = keep current value. Frozen after the first usage.
    /// </summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>
    /// New valid-until timestamp. Null = keep current value. After the first usage it may only be extended.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; init; }

    public IReadOnlyList<Guid>? ApplicableRouteIds { get; init; }
}
