using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record TicketHistoryDetailsDto(
    Guid? BookingGroupId,
    string? TripDirection,
    string? RouteName,
    IReadOnlyList<PassengerHistoryTicketDto> Tickets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    PassengerHistoryVehicleDto? Vehicle = null,
    PassengerHistoryPointDto? PickupPoint = null,
    PassengerHistoryPointDto? DropoffPoint = null);
