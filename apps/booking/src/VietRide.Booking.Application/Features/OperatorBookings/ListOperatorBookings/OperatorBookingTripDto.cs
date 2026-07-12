namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record OperatorBookingTripDto(
    string? RouteName,
    string? OriginName,
    string? DestinationName,
    DateTimeOffset? DepartureAt);
