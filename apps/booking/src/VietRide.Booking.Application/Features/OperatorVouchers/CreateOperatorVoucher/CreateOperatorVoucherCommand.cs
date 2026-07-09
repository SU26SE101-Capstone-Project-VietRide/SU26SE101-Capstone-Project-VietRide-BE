using MediatR;

namespace VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;

/// <summary>
/// Command for POST /v1/operator/vouchers — creates an operator-owned OPERATOR_FUNDED voucher.
/// <para>
/// <see cref="OwnerOperatorId"/> is set server-side from the caller's JWT (OPERATOR_ADMIN).
/// <see cref="FundingType"/> is always forced to OPERATOR_FUNDED; any other value is rejected
/// with 422 VOUCHER_FORBIDDEN_FUNDING. <see cref="ApplicableOperatorIds"/> is always forced to
/// [<see cref="OwnerOperatorId"/>] — no consent fan-out (self-consented).
/// </para>
/// </summary>
public sealed record CreateOperatorVoucherCommand(
    /// <summary>Null → auto-generate 8-char uppercase base32 code (v7:4564).</summary>
    string? Code,
    string Name,
    /// <summary>PERCENT_OFF or FIXED_AMOUNT.</summary>
    string Type,
    long Value,
    long MinOrderAmount,
    long? MaxDiscountAmount,
    int? TotalUsageLimit,
    int? PerUserLimit,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    /// <summary>BOOKING, PARCEL, or both. Null defaults to BOOKING.</summary>
    IReadOnlyList<string>? ApplicableServices,
    IReadOnlyList<Guid>? ApplicableRouteIds,
    /// <summary>
    /// Optional. If supplied and not OPERATOR_FUNDED → handler throws 422 VOUCHER_FORBIDDEN_FUNDING.
    /// Server always forces OPERATOR_FUNDED regardless.
    /// </summary>
    string? FundingType,
    /// <summary>Set server-side from the JWT sub claim (OPERATOR_ADMIN caller).</summary>
    Guid OwnerOperatorId,
    /// <summary>JWT sub claim of the OPERATOR_ADMIN calling the endpoint.</summary>
    Guid CreatedByUserId) : IRequest<CreateOperatorVoucherResult>;
