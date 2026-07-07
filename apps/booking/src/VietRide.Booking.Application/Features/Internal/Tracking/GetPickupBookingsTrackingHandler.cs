using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Internal.Tracking;

public sealed class GetPickupBookingsTrackingHandler
    : IRequestHandler<GetPickupBookingsTrackingQuery, PickupBookingsTrackingResponse>
{
    private readonly IBookingRepository bookingRepository;

    public GetPickupBookingsTrackingHandler(IBookingRepository bookingRepository)
    {
        this.bookingRepository = bookingRepository;
    }

    public async Task<PickupBookingsTrackingResponse> Handle(
        GetPickupBookingsTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var bookings = await bookingRepository.QueryNoTracking()
            .Where(booking =>
                booking.TripId == request.TripId
                && booking.PickupStopId == request.StopId
                && booking.Status == BookingStatus.CONFIRMED
                && booking.Tickets.Any(ticket =>
                    ticket.Status == TicketStatus.ISSUED || ticket.Status == TicketStatus.USED))
            .Select(booking => new PickupBookingTrackingDto(
                booking.Id,
                booking.PassengerUserId,
                request.StopId,
                "CONFIRMED",
                null))
            .ToArrayAsync(cancellationToken);

        return new PickupBookingsTrackingResponse(bookings);
    }
}
