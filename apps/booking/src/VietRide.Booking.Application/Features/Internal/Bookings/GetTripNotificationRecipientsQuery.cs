using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetTripNotificationRecipientsQuery(string TripId)
    : IQuery<TripNotificationRecipientsDto>;
