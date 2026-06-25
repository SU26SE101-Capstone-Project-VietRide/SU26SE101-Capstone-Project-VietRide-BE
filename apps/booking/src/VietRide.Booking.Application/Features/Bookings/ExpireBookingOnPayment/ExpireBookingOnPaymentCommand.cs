using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;

public sealed record ExpireBookingOnPaymentCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId) : IRequest<bool>;
