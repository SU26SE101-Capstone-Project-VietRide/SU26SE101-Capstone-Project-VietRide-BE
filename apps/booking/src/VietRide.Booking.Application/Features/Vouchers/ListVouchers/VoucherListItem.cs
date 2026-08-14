namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Single row in voucher list endpoints.
/// </summary>
public sealed record VoucherListItem(
    Guid Id,
    string Code,
    string Name,
    string Type,
    long Value,
    long MinOrderAmount,
    long? MaxDiscountAmount,
    int? TotalUsageLimit,
    int? PerUserLimit,
    bool NewUserOnly,
    IReadOnlyList<string> ApplicableServices,
    IReadOnlyList<string> ApplicablePaymentMethods,
    IReadOnlyList<Guid> ApplicableOperatorIds,
    IReadOnlyList<Guid> ApplicableRouteIds,
    string FundingType,
    Guid? OwnerOperatorId,
    bool IsActive,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset CreatedAt,
    int UsedCount);
