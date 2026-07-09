using MediatR;

namespace VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;

/// <summary>
/// Command for PATCH /v1/admin/vouchers/{id} — partial update of a platform-owned voucher.
/// </summary>
public sealed record UpdateAdminVoucherCommand(
    Guid VoucherId,
    string? Name,
    long? Value,
    long? MinOrderAmount,
    long? MaxDiscountAmount,
    int? TotalUsageLimit,
    int? PerUserLimit,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    bool? NewUserOnly,
    IReadOnlyList<string>? ApplicablePaymentMethods,
    IReadOnlyList<string>? ApplicableServices,
    IReadOnlyList<Guid>? ApplicableRouteIds) : IRequest<UpdateAdminVoucherResult>;
