namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed record SeededFallback(
    Guid BookingId,
    Guid TripId,
    Guid UserId,
    Guid ActionId,
    Guid DisabledStopId,
    Guid FallbackStationId,
    string AffectedField);
