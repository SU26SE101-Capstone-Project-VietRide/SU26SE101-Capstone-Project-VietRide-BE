using MediatR;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed class InternalRecordVoucherUsageCommandHandler
    : IRequestHandler<InternalRecordVoucherUsageCommand, InternalRecordVoucherUsageResult>
{
    private readonly IVoucherService _voucherService;

    public InternalRecordVoucherUsageCommandHandler(IVoucherService voucherService)
    {
        _voucherService = voucherService;
    }

    public async Task<InternalRecordVoucherUsageResult> Handle(
        InternalRecordVoucherUsageCommand request,
        CancellationToken cancellationToken)
    {
        var usageId = await _voucherService.RecordUsageForReferenceAsync(
            request.VoucherId,
            request.UserId,
            request.ReferenceType,
            request.ReferenceId,
            null,
            Money.FromRaw(request.DiscountAmount),
            cancellationToken);

        return new InternalRecordVoucherUsageResult(usageId);
    }
}
