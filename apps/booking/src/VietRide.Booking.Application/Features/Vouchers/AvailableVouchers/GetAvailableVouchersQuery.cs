using MediatR;

namespace VietRide.Booking.Application.Features.Vouchers.AvailableVouchers;

public sealed record GetAvailableVouchersQuery(
    Guid UserId,
    string Service,
    Guid? TripId,
    Guid? OperatorId,
    Guid? RouteId,
    string? PaymentMethod,
    long? OrderAmount) : IRequest<IReadOnlyList<AvailableVoucherItem>>;
