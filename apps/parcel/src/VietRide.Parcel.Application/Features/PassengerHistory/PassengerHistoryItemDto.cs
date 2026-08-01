using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record PassengerHistoryItemDto(
    string Type,
    Guid Id,
    string Code,
    Guid TripId,
    string Status,
    DateTimeOffset CreatedAt,
    long TotalAmount,
    string? OriginName,
    string? DestinationName,
    DateTimeOffset? DepartureDateTime,
    DateTimeOffset? EstimatedArrivalTime,
    TicketHistoryDetailsDto? Ticket,
    ParcelHistoryDetailsDto? Parcel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? PaymentRedirectUrl = null);
