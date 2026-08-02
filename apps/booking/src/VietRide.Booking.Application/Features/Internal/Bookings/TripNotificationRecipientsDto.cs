namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record TripNotificationRecipientsDto(
    Guid TripId,
    IReadOnlyList<TripNotificationRecipientDto> Recipients);
