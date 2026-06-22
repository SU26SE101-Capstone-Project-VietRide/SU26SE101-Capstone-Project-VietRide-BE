using MediatR;

namespace VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;

/// <summary>
/// Command for DELETE /v1/operator/vouchers/{id} — soft-deletes an operator-owned voucher
/// by setting <c>deleted_at</c> (ADR 0003). The code becomes reusable after deletion
/// (partial unique index <c>uq_vouchers_code WHERE deleted_at IS NULL</c>).
/// </summary>
public sealed record DeleteOperatorVoucherCommand(
    Guid VoucherId,
    /// <summary>Caller's operatorId from JWT — used for tenant-isolation check.</summary>
    Guid CallerOperatorId) : IRequest<DeleteOperatorVoucherResult>;
