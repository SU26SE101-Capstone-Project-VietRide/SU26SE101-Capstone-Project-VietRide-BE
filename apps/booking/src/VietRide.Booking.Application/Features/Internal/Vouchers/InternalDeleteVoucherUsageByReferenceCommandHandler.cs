using MediatR;
using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed class InternalDeleteVoucherUsageByReferenceCommandHandler
    : IRequestHandler<InternalDeleteVoucherUsageByReferenceCommand, Unit>
{
    private readonly IVoucherService _voucherService;

    public InternalDeleteVoucherUsageByReferenceCommandHandler(IVoucherService voucherService)
    {
        _voucherService = voucherService;
    }

    public async Task<Unit> Handle(
        InternalDeleteVoucherUsageByReferenceCommand request,
        CancellationToken cancellationToken)
    {
        await _voucherService.CompensateByReferenceAsync(request.ReferenceType, request.ReferenceId, cancellationToken);
        return Unit.Value;
    }
}
