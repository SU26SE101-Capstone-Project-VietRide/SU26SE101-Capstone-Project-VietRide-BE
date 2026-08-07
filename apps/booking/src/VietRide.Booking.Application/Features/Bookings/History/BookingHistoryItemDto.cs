using System.Text.Json.Serialization;

namespace VietRide.Booking.Application.Features.Bookings.History;

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? PaymentRedirectUrl = null,
    Guid? DropoffStationId = null,
    Guid? DropoffStopId = null);
