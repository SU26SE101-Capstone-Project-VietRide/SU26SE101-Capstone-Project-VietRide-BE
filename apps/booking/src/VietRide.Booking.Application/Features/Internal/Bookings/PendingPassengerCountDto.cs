namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record PendingPassengerCountDto(
    Guid TripId,
    Guid StopId,
    int PendingPassengerCount);
