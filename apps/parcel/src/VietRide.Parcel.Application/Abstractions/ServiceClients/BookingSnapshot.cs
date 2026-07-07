namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingSnapshot(
    Guid BookingId,
    Guid UserId,
    Guid TripId,
    string Status,
    int ActiveTicketCount = 0,
    IReadOnlyList<BookingTicketSnapshot>? Tickets = null);

public sealed record BookingTicketSnapshot(
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string Status);
