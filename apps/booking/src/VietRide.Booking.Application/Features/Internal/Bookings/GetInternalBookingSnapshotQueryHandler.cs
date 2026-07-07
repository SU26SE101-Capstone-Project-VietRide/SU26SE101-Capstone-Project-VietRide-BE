using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetInternalBookingSnapshotQueryHandler
    : IRequestHandler<GetInternalBookingSnapshotQuery, InternalBookingSnapshotDto>
{
    private readonly IBookingRepository _bookings;

    public GetInternalBookingSnapshotQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<InternalBookingSnapshotDto> Handle(
        GetInternalBookingSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _bookings.QueryNoTracking()
            .Where(booking => booking.Id == request.BookingId)
            .Select(booking => new InternalBookingSnapshotDto(
                booking.Id,
                booking.PassengerUserId,
                booking.TripId,
                booking.Status.ToString(),
                booking.Tickets.Count(ticket =>
                    ticket.Status == TicketStatus.ISSUED || ticket.Status == TicketStatus.USED),
                booking.Tickets
                    .Where(ticket => ticket.Status == TicketStatus.ISSUED || ticket.Status == TicketStatus.USED)
                    .OrderBy(ticket => ticket.SeatNumber)
                    .Select(ticket => new InternalBookingTicketDto(
                        ticket.Id,
                        ticket.TicketCode.Value,
                        ticket.SeatNumber,
                        ticket.Status.ToString()))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
        }

        return snapshot;
    }
}
