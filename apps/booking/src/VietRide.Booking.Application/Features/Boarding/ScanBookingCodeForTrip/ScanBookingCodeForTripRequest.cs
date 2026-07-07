namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed record ScanBookingCodeForTripRequest(
    string? TicketCode,
    string? BookingCode);
