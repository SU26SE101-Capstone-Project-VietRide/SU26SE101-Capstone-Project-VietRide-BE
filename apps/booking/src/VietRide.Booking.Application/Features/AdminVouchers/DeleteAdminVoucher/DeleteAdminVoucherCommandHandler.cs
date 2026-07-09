using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;

/// <summary>
/// Handles DELETE /v1/admin/vouchers/{id} for platform-owned vouchers only.
/// </summary>
public sealed class DeleteAdminVoucherCommandHandler
    : IRequestHandler<DeleteAdminVoucherCommand, DeleteAdminVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly IClock _clock;
    private readonly ILogger<DeleteAdminVoucherCommandHandler> _logger;

    public DeleteAdminVoucherCommandHandler(
        IVoucherRepository vouchers,
        IClock clock,
        ILogger<DeleteAdminVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DeleteAdminVoucherResult> Handle(
        DeleteAdminVoucherCommand request,
        CancellationToken cancellationToken)
    {
        var voucher = await _vouchers.FindPlatformByIdIgnoringSoftDeleteAsync(
            request.VoucherId,
            cancellationToken);

        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{request.VoucherId}' not found.");
        }

        if (voucher.DeletedAt.HasValue)
        {
            _logger.LogDebug(
                "Admin platform voucher {VoucherId} is already soft-deleted; treating DELETE as no-op.",
                voucher.Id);

            return new DeleteAdminVoucherResult(voucher.Id, voucher.DeletedAt.Value);
        }

        var deletedAt = _clock.UtcNow;
        voucher.SoftDelete(deletedAt);
        _vouchers.Update(voucher);

        _logger.LogInformation("Admin platform voucher {VoucherId} soft-deleted.", voucher.Id);

        return new DeleteAdminVoucherResult(voucher.Id, deletedAt);
    }
}
