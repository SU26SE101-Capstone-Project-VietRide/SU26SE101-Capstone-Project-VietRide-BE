namespace VietRide.Booking.Application.Features.Bookings.CreateBooking;

/// <summary>
/// Response DTO for POST /v1/bookings (201 Created).
/// Shape per VietRide_API_Contract_v1.md lines 706-721.
/// </summary>
public sealed record CreateBookingResult(
    Guid BookingId,
    string BookingCode,
    string Status,
    long TotalAmount,
    long DiscountAmount,
    string? PaymentRedirectUrl,
    IReadOnlyList<CreateBookingTicketResult> Tickets);

public sealed record CreateBookingTicketResult(
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string Status,
    long FareAmount,
    long DiscountAmount,
    long PaidAmount);
