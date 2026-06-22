namespace VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;

/// <summary>
/// Response payload for POST /v1/operator/vouchers/{id}/activate and /deactivate
/// (ADR 0004 ApiResponse envelope).
/// </summary>
public sealed record SetOperatorVoucherActiveResult(
    Guid Id,
    bool IsActive);
