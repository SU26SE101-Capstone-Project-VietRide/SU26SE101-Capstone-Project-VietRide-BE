using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;

/// <summary>
/// Handles DELETE /v1/operator/vouchers/{id} — soft-deletes an operator-owned voucher.
/// Returns <see cref="DeleteOperatorVoucherResult"/> with <c>{ id, deletedAt }</c>
/// (contract mandates 200 ApiResponse, not 204 NoContent).
/// <para>
/// Idempotency: deleting an already-soft-deleted voucher owned by the caller is a no-op
/// (returns the existing <c>DeletedAt</c> timestamp). This is implemented by querying with
/// IgnoreQueryFilters so that a previously soft-deleted voucher is still found; the handler
/// checks <c>DeletedAt</c> and short-circuits without calling <c>SoftDelete</c> again.
/// </para>
/// <para>
/// Tenant isolation: a non-existent voucher OR a voucher owned by a different operator
/// returns <c>null</c> from the repository → 404 VOUCHER_NOT_FOUND. The caller never
/// learns whether the voucher exists for another operator (existence not leaked).
/// </para>
/// </summary>
public sealed class DeleteOperatorVoucherCommandHandler
    : IRequestHandler<DeleteOperatorVoucherCommand, DeleteOperatorVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly IClock _clock;
    private readonly ILogger<DeleteOperatorVoucherCommandHandler> _logger;

    public DeleteOperatorVoucherCommandHandler(
        IVoucherRepository vouchers,
        IClock clock,
        ILogger<DeleteOperatorVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DeleteOperatorVoucherResult> Handle(
        DeleteOperatorVoucherCommand request,
        CancellationToken cancellationToken)
    {
        // Use IgnoreQueryFilters so that an already-soft-deleted voucher owned by the caller
        // is still found. A non-existent or cross-operator voucher returns null → 404.
        var voucher = await _vouchers.FindByIdAndOwnerIgnoringSoftDeleteAsync(
            request.VoucherId,
            request.CallerOperatorId,
            cancellationToken);

        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{request.VoucherId}' not found.");
        }

        // Idempotency: already soft-deleted by this operator → no-op, return existing deletedAt.
        if (voucher.DeletedAt.HasValue)
        {
            _logger.LogDebug(
                "Operator voucher {VoucherId} is already soft-deleted; treating DELETE as no-op.",
                voucher.Id);
            return new DeleteOperatorVoucherResult(voucher.Id, voucher.DeletedAt.Value);
        }

        var deletedAt = _clock.UtcNow;
        voucher.SoftDelete(deletedAt);
        _vouchers.Update(voucher);

        _logger.LogInformation(
            "Operator voucher {VoucherId} soft-deleted by operator {OperatorId}.",
            voucher.Id,
            request.CallerOperatorId);

        return new DeleteOperatorVoucherResult(voucher.Id, deletedAt);
    }
}
