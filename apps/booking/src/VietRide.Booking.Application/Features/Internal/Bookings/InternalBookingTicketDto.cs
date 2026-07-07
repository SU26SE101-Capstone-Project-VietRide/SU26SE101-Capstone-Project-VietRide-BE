namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record InternalBookingTicketDto(
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string Status);
