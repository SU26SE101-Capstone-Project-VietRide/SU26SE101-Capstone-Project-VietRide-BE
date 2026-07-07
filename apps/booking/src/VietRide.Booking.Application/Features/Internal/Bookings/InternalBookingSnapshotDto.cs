namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record InternalBookingSnapshotDto(
    Guid BookingId,
    Guid UserId,
    Guid TripId,
    string Status,
    int ActiveTicketCount,
    IReadOnlyList<InternalBookingTicketDto> Tickets);
