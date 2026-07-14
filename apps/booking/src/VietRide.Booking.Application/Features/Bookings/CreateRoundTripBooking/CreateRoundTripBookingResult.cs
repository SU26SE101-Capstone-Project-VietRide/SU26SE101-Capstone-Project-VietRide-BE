namespace VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;

/// <summary>
/// Response DTO for POST /v1/bookings/round-trip (201 Created).
/// </summary>
public sealed record CreateRoundTripBookingResult
{
    public CreateRoundTripBookingResult(
        Guid bookingGroupId,
        RoundTripBookingResult outbound,
        RoundTripBookingResult @return,
        long grandTotal,
        Guid? paymentId,
        string status,
        string? paymentRedirectUrl)
    {
        BookingGroupId = bookingGroupId;
        Outbound = outbound;
        Return = @return;
        GrandTotal = grandTotal;
        PaymentId = paymentId;
        Status = status;
        PaymentRedirectUrl = paymentRedirectUrl;
    }

    public Guid BookingGroupId { get; init; }

    public RoundTripBookingResult Outbound { get; init; }

    public RoundTripBookingResult Return { get; init; }

    public long GrandTotal { get; init; }

    public Guid? PaymentId { get; init; }

    public string Status { get; init; }

    public string? PaymentRedirectUrl { get; init; }

    public sealed record RoundTripBookingResult(
        Guid BookingId,
        string BookingCode,
        long TotalAmount,
        long DiscountAmount,
        IReadOnlyList<RoundTripTicketResult> Tickets);

    public sealed record RoundTripTicketResult(
        Guid TicketId,
        string TicketCode,
        string SeatNumber,
        string Status,
        long FareAmount,
        long DiscountAmount,
        long PaidAmount);
}
