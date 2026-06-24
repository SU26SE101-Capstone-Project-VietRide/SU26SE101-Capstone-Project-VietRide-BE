using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;

public sealed record ConfirmBookingOnPaymentCommand(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    long Amount) : IRequest<bool>;
