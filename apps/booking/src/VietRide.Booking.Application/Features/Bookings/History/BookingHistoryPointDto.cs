namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed record BookingHistoryPointDto(
    string Type,
    Guid Id,
    string? DisplayName,
    string? Address,
    DateTimeOffset? PlannedAt);
