using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Bookings.GetBookingStatus;

/// <summary>Returns Booking-owned status to the passenger owner or booking operator.</summary>
public sealed class GetBookingStatusQueryHandler : IRequestHandler<GetBookingStatusQuery, GetBookingStatusResult>
{
    private readonly IBookingRepository _bookings;

    public GetBookingStatusQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<GetBookingStatusResult> Handle(
        GetBookingStatusQuery request,
        CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
        }

        if (request.PassengerUserId == booking.PassengerUserId || request.OperatorId == booking.OperatorId)
        {
            return new GetBookingStatusResult(booking.Id, booking.Status.ToString());
        }

        if (request.OperatorId is not null)
        {
            throw new ForbiddenException("FORBIDDEN", "Booking belongs to another operator.");
        }

        throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
    }
}
