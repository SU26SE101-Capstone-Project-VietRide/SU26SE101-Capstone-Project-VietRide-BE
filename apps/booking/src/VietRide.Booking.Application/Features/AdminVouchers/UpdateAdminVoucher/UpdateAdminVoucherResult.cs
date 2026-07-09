namespace VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;

/// <summary>
/// Response payload for PATCH /v1/admin/vouchers/{id}.
/// </summary>
public sealed record UpdateAdminVoucherResult(
    Guid Id,
    string Code,
    string Name,
    string Type,
    long Value,
    string FundingType,
    Guid? OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    bool NewUserOnly,
    IReadOnlyList<string> ApplicablePaymentMethods,
    IReadOnlyList<string> ApplicableServices,
    IReadOnlyList<Guid> ApplicableRouteIds);
