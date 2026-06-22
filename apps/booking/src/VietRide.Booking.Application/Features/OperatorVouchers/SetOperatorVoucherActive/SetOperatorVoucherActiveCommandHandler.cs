using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;

/// <summary>
/// Handles POST /v1/operator/vouchers/{id}/activate and /deactivate.
/// Returns <see cref="SetOperatorVoucherActiveResult"/> with <c>{ id, isActive }</c>
/// (contract mandates 200 ApiResponse with body, not empty Ok()).
/// Cross-operator access → 404 VOUCHER_NOT_FOUND (tenant isolation).
/// Behavior-idempotent: activate on active / deactivate on inactive → no error.
/// </summary>
public sealed class SetOperatorVoucherActiveCommandHandler
    : IRequestHandler<SetOperatorVoucherActiveCommand, SetOperatorVoucherActiveResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly ILogger<SetOperatorVoucherActiveCommandHandler> _logger;

    public SetOperatorVoucherActiveCommandHandler(
        IVoucherRepository vouchers,
        ILogger<SetOperatorVoucherActiveCommandHandler> logger)
    {
        _vouchers = vouchers;
        _logger = logger;
    }

    public async Task<SetOperatorVoucherActiveResult> Handle(
        SetOperatorVoucherActiveCommand request,
        CancellationToken cancellationToken)
    {
        var voucher = await _vouchers.FindByIdAndOwnerAsync(
            request.VoucherId,
            request.CallerOperatorId,
            cancellationToken);

        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{request.VoucherId}' not found.");
        }

        if (request.Activate)
            voucher.Activate();
        else
            voucher.Deactivate();

        _vouchers.Update(voucher);

        _logger.LogInformation(
            "Operator voucher {VoucherId} {Action} by operator {OperatorId}.",
            voucher.Id,
            request.Activate ? "activated" : "deactivated",
            request.CallerOperatorId);

        return new SetOperatorVoucherActiveResult(voucher.Id, voucher.IsActive);
    }
}
