using MediatR;

namespace VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;

/// <summary>
/// Query for GET /v1/admin/vouchers/{voucherId}/consents — returns all operator-consent rows
/// for a given voucher for admin governance view (v7:702-704).
/// </summary>
public sealed record ListAdminVoucherConsentsQuery(
    Guid VoucherId) : IRequest<AdminVoucherConsentsResult>;
