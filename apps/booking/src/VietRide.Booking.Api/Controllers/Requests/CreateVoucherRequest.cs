namespace VietRide.Booking.Api.Controllers.Requests;

/// <summary>
/// Request body for POST /v1/admin/vouchers.
/// <para>
/// <c>code</c> is optional — null triggers auto-generation of an 8-char uppercase base32 code (v7:4564).
/// For <c>OPERATOR_FUNDED</c> vouchers, <c>applicableOperatorIds</c> is required (Q3 RESOLVED).
/// </para>
/// </summary>
public sealed class CreateVoucherRequest
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

    /// <summary>Required for OPERATOR_FUNDED; optional for VIETRIDE_FUNDED (null = all operators).</summary>
    public IReadOnlyList<Guid>? ApplicableOperatorIds { get; init; }

    public IReadOnlyList<Guid>? ApplicableRouteIds { get; init; }

    /// <summary>VIETRIDE_FUNDED or OPERATOR_FUNDED.</summary>
    public string FundingType { get; init; } = string.Empty;
}
