using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;

public sealed record AcceptStopDisabledFallbackCommand(
    Guid BookingId,
    Guid ActionId,
    Guid PassengerUserId,
    string IdempotencyKey) : IRequest<AcceptStopDisabledFallbackResult>;
