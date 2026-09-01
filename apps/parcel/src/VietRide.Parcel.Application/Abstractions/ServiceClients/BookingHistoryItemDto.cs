namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryItemDto(
    Guid BookingId,
    string BookingCode,
    Guid TripId,
    string Status,
    DateTimeOffset CreatedAt,
    long TotalAmount,
    string? OriginName,
    string? DestinationName,
    DateTimeOffset? DepartureDateTime,
    Guid? BookingGroupId,
    string? TripDirection,
    string? RouteName,
    IReadOnlyList<BookingHistoryTicketDto> Tickets,
    string? PaymentRedirectUrl = null,
    Guid? DropoffStationId = null,
    Guid? DropoffStopId = null,
    BookingHistoryVehicleDto? Vehicle = null,
    BookingHistoryPointDto? PickupPoint = null,
    BookingHistoryPointDto? DropoffPoint = null);
