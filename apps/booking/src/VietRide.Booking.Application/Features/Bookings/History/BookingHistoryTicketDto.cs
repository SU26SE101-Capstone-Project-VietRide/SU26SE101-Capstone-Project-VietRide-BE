namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed record BookingHistoryTicketDto(
    Guid TicketId,
    string TicketCode,
    string? SeatNumber,
    string Status,
    long PaidAmount);
