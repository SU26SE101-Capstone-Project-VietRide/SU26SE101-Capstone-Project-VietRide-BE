using MediatR;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed class InternalValidateVoucherCommandHandler
    : IRequestHandler<InternalValidateVoucherCommand, InternalValidateVoucherResult>
{
    private readonly IVoucherService _voucherService;
    private readonly IClock _clock;

    public InternalValidateVoucherCommandHandler(IVoucherService voucherService, IClock clock)
    {
        _voucherService = voucherService;
        _clock = clock;
    }

    public async Task<InternalValidateVoucherResult> Handle(
        InternalValidateVoucherCommand request,
        CancellationToken cancellationToken)
    {
        var result = await _voucherService.ValidateAndComputeDiscountAsync(
            request.VoucherCode,
            request.OperatorId,
            request.RouteId,
            request.UserId,
            Money.FromRaw(request.OrderAmount),
            _clock.UtcNow,
            cancellationToken,
            request.Service,
            request.PaymentMethod);

        return new InternalValidateVoucherResult(result.VoucherId, result.Discount.Amount);
    }
}
