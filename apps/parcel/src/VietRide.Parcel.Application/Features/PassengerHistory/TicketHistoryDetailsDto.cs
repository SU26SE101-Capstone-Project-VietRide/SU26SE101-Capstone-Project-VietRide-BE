namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record TicketHistoryDetailsDto(
    Guid? BookingGroupId,
    string? TripDirection,
    string? RouteName,
    IReadOnlyList<PassengerHistoryTicketDto> Tickets);
