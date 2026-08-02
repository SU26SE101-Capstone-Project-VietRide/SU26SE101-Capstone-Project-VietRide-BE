using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetTripNotificationRecipientsQueryHandler(IBookingRepository bookings)
    : IRequestHandler<GetTripNotificationRecipientsQuery, TripNotificationRecipientsDto>
{
    public Task<TripNotificationRecipientsDto> Handle(
        GetTripNotificationRecipientsQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.TripId, out var tripId) || tripId == Guid.Empty)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "tripId is required and must be a non-empty UUID.");
        }

        return bookings.GetTripNotificationRecipientsAsync(tripId, cancellationToken);
    }
}
