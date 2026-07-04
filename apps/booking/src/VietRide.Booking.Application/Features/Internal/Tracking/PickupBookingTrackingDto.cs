namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed record PickupBookingTrackingDto(
    Guid BookingId,
    Guid? PassengerUserId,
    Guid StopId,
    string Status,
    string? PickupStatus = null);
