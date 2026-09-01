namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record PassengerHistoryTicketDto(
    Guid TicketId,
    string TicketCode,
    string? SeatNumber,
    string Status,
    long PaidAmount);
