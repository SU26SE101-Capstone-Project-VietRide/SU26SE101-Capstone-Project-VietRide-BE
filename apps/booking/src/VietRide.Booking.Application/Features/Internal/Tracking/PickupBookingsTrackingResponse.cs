namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed record PickupBookingsTrackingResponse(
    IReadOnlyList<PickupBookingTrackingDto> Bookings);
