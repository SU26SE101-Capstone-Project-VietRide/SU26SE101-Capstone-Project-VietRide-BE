using MediatR;

namespace VietRide.Booking.Application.Features.Vouchers.CreateVoucher;

/// <summary>
/// Command for POST /v1/admin/vouchers — creates a platform voucher (SYSTEM_ADMIN only).
/// <para>
/// <see cref="OwnerOperatorId"/> is always <c>null</c> for admin-created vouchers.
/// For OPERATOR_FUNDED, <see cref="ApplicableOperatorIds"/> must be non-null/non-empty
/// (Q3 RESOLVED — null is rejected with VALIDATION_ERROR).
/// </para>
/// <para>
/// <see cref="Type"/> and <see cref="FundingType"/> are string-valued (PERCENT_OFF/FIXED_AMOUNT and
/// VIETRIDE_FUNDED/OPERATOR_FUNDED) — the handler parses them to domain enums so the Api layer
/// does not take a direct dependency on the Domain assembly (NetArchTest Clean Architecture rule).
/// </para>
/// </summary>
public sealed record CreateVoucherCommand(
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
    IReadOnlyList<Guid>? ApplicableOperatorIds,
    IReadOnlyList<Guid>? ApplicableRouteIds,
    /// <summary>VIETRIDE_FUNDED or OPERATOR_FUNDED.</summary>
    string FundingType,
    /// <summary>JWT sub claim of the SYSTEM_ADMIN calling the endpoint.</summary>
    Guid CreatedByUserId) : IRequest<CreateVoucherResult>;
