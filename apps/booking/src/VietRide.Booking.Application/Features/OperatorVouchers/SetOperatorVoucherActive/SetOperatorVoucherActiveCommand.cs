using MediatR;

namespace VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;

/// <summary>
/// Command for POST /v1/operator/vouchers/{id}/activate and /deactivate.
/// Flips <see cref="Domain.Entities.Voucher.IsActive"/> (<see cref="Domain.Interfaces.IActivatable"/>).
/// Behavior-idempotent: calling activate on an already-active voucher is a no-op.
/// No Idempotency-Key required (BSOT sec 5.6 activation precedent).
/// </summary>
public sealed record SetOperatorVoucherActiveCommand(
    Guid VoucherId,
    /// <summary>Caller's operatorId from JWT — used for tenant-isolation check.</summary>
    Guid CallerOperatorId,
    /// <summary><c>true</c> = activate; <c>false</c> = deactivate.</summary>
    bool Activate) : IRequest<SetOperatorVoucherActiveResult>;
