namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed record BookingHistoryShuttleRequestDto(
    string Direction,
    string Address,
    decimal Latitude,
    decimal Longitude,
    int? RoadDistanceMeters,
    bool IsActive,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CancelledAt);
