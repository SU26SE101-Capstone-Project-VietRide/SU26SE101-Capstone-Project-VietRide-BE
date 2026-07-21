namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryTicketDto(
    Guid TicketId,
    string TicketCode,
    string SeatNumber,
    string Status,
    long PaidAmount);
