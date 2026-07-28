namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed record OperatorBookingSeatDto(
    Guid PassengerRecordId,
    Guid TicketId,
    string TicketCode,
    string? SeatNumber,
    string TicketStatus,
    string BoardingStatus);
