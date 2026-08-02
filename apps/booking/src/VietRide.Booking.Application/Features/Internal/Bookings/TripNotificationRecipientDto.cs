namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record TripNotificationRecipientDto(
    Guid BookingId,
    Guid UserId,
    string Status);
