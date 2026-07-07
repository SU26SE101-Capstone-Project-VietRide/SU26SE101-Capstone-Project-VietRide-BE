namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed record ScanBookingCodePassengerItem(
    Guid PassengerRecordId,
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string BoardingStatus);
