namespace VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;

public sealed record TickPassengerBoardedResult(
    Guid PassengerRecordId,
    string BoardingStatus,
    DateTimeOffset BoardedAt,
    Guid? BoardedAtStopId,
    Guid TicketId,
    string TicketCode,
    string TicketStatus);
