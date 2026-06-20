namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Request body for POST /v1/operator/vouchers.
/// <para>
/// <c>fundingType</c> is FORCED to OPERATOR_FUNDED server-side — supplying any other value returns
/// 422 VOUCHER_FORBIDDEN_FUNDING. <c>applicableOperatorIds</c> is FORCED to the caller's operatorId
/// (self-consented, no consent fan-out). Both fields are omitted from this DTO.
/// </para>
/// </summary>
public sealed class CreateOperatorVoucherRequest
{
    /// <summary>Optional. Null → auto-generate unique 8-char base32 code.</summary>
    public string? Code { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>PERCENT_OFF or FIXED_AMOUNT.</summary>
    public string Type { get; init; } = string.Empty;

    public long Value { get; init; }

    public long MinOrderAmount { get; init; }

    public long? MaxDiscountAmount { get; init; }

    public int? TotalUsageLimit { get; init; }

    public int? PerUserLimit { get; init; }

    public DateTimeOffset ValidFrom { get; init; }

    public DateTimeOffset ValidUntil { get; init; }

    public IReadOnlyList<Guid>? ApplicableRouteIds { get; init; }

    /// <summary>
    /// Optional. If supplied, MUST be OPERATOR_FUNDED — any other value → 422 VOUCHER_FORBIDDEN_FUNDING.
    /// Server always forces OPERATOR_FUNDED regardless.
    /// </summary>
    public string? FundingType { get; init; }
}
