using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.MarkBookingRefunded;

public sealed record MarkBookingRefundedCommand(
    Guid UserId,
    long Amount,
    string ReferenceType,
    Guid ReferenceId) : IRequest<bool>;
