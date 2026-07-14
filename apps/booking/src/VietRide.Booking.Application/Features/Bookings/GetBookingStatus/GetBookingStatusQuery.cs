using MediatR;

namespace VietRide.Booking.Application.Features.Bookings.GetBookingStatus;

/// <summary>Query for the minimal booking payment-status poll.</summary>
public sealed record GetBookingStatusQuery(
    Guid BookingId,
    Guid? PassengerUserId,
    Guid? OperatorId) : IRequest<GetBookingStatusResult>;
