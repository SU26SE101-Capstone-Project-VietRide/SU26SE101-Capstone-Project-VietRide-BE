using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed record InternalValidateVoucherCommand(
    string VoucherCode,
    Guid OperatorId,
    Guid RouteId,
    Guid UserId,
    long OrderAmount,
    string Service,
    string? PaymentMethod) : IRequest<InternalValidateVoucherResult>;

public sealed record InternalValidateVoucherResult(Guid VoucherId, long DiscountAmount);
