using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed class GetBookingTrackingAuthorizationHandler
    : IRequestHandler<GetBookingTrackingAuthorizationQuery, TrackingBookingAuthorizationResponse>
{
    private static readonly BookingStatus[] TrackableStatuses =
    [
        BookingStatus.CONFIRMED,
        BookingStatus.COMPLETED,
        BookingStatus.DISRUPTED,
        BookingStatus.PARTIAL_NO_SHOW,
    ];

    private readonly IBookingRepository bookingRepository;

    public GetBookingTrackingAuthorizationHandler(IBookingRepository bookingRepository)
    {
        this.bookingRepository = bookingRepository;
    }

    public async Task<TrackingBookingAuthorizationResponse> Handle(
        GetBookingTrackingAuthorizationQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Role, "PASSENGER", StringComparison.OrdinalIgnoreCase)
            || !request.UserId.HasValue)
        {
            return new TrackingBookingAuthorizationResponse(false, Error: "ACCESS_DENIED");
        }

        var allowed = await bookingRepository.QueryNoTracking().AnyAsync(booking =>
            booking.TripId == request.TripId
            && booking.PassengerUserId == request.UserId.Value
            && TrackableStatuses.Contains(booking.Status)
            && booking.Tickets.Any(ticket =>
                ticket.Status == TicketStatus.ISSUED || ticket.Status == TicketStatus.USED),
            cancellationToken);

        return allowed
            ? new TrackingBookingAuthorizationResponse(true, "BOOKING_OWNER")
            : new TrackingBookingAuthorizationResponse(false, Error: "ACCESS_DENIED");
    }
}
