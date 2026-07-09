using MediatR;

namespace VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;

/// <summary>
/// Command for DELETE /v1/admin/vouchers/{id} — soft-deletes a platform-owned voucher.
/// </summary>
public sealed record DeleteAdminVoucherCommand(Guid VoucherId) : IRequest<DeleteAdminVoucherResult>;
