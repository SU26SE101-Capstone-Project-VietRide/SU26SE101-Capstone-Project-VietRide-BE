using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed class GetBookingHistoryQueryHandler
    : IRequestHandler<GetBookingHistoryQuery, PagedResult<BookingHistoryItemDto>>
{
    private readonly IBookingRepository _bookings;

    public GetBookingHistoryQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<PagedResult<BookingHistoryItemDto>> Handle(
        GetBookingHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var range = BookingHistoryDateRange.Parse(request.From, request.To);
        var status = request.Status is null
            ? (BookingStatus?)null
            : Enum.Parse<BookingStatus>(request.Status, true);
        var page = await _bookings.ListPassengerHistoryAsync(
            request.UserId,
            status,
            range.From,
            range.To,
            request.Page,
            request.PageSize,
            cancellationToken);

        var items = page.Items.Select(booking => new BookingHistoryItemDto(
            booking.Id,
            booking.BookingCode.Value,
            booking.TripId,
            booking.Status.ToString(),
            booking.CreatedAt,
            booking.TotalAmount.Amount,
            booking.TripSnapshotOriginName,
            booking.TripSnapshotDestName,
            booking.TripCurrentDeparture ?? booking.TripSnapshotDeparture,
            booking.BookingGroupId,
            booking.TripDirection?.ToString(),
            booking.TripSnapshotRouteName,
            booking.Tickets
                .OrderBy(ticket => ticket.SeatNumber, StringComparer.Ordinal)
                .ThenBy(ticket => ticket.Id)
                .Select(ticket => new BookingHistoryTicketDto(
                    ticket.Id,
                    ticket.TicketCode.Value,
                    ticket.SeatNumber,
                    ticket.Status.ToString(),
                    ticket.PaidAmount.Amount))
                .ToList()))
            .ToList();

        return PagedResult<BookingHistoryItemDto>.Create(
            items,
            page.Page,
            page.PageSize,
            page.TotalItems);
    }
}
