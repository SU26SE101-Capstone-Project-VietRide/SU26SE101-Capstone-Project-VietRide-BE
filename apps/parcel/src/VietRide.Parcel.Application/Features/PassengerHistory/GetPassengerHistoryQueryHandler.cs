using MediatR;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.History;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed class GetPassengerHistoryQueryHandler
    : IRequestHandler<GetPassengerHistoryQuery, PagedResult<PassengerHistoryItemDto>>
{
    private readonly IBookingServiceClient _bookings;
    private readonly SentParcelHistoryReader _parcels;

    public GetPassengerHistoryQueryHandler(
        IBookingServiceClient bookings,
        SentParcelHistoryReader parcels)
    {
        _bookings = bookings;
        _parcels = parcels;
    }

    public async Task<PagedResult<PassengerHistoryItemDto>> Handle(
        GetPassengerHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Type.Equals("TICKET", StringComparison.OrdinalIgnoreCase))
            return await GetTicketHistoryAsync(request, cancellationToken);

        var parcelPage = await _parcels.ReadAsync(
            request.UserId,
            request.Status,
            request.From,
            request.To,
            request.Page,
            request.PageSize,
            cancellationToken);
        var parcelItems = parcelPage.Items.Select(parcel => new PassengerHistoryItemDto(
            "PARCEL",
            parcel.ParcelId,
            parcel.ParcelCode,
            parcel.TripId,
            parcel.Status,
            parcel.CreatedAt,
            parcel.TotalAmount,
            parcel.OriginName,
            parcel.DestinationName,
            parcel.DepartureDateTime,
            parcel.EstimatedArrivalTime,
            null,
            new ParcelHistoryDetailsDto(
                parcel.BookingId,
                parcel.RecipientName,
                parcel.SizeCategory,
                parcel.PhotoUrl,
                parcel.DeliveryMethod)))
            .ToList();

        return PagedResult<PassengerHistoryItemDto>.Create(
            parcelItems,
            parcelPage.Page,
            parcelPage.PageSize,
            parcelPage.TotalItems);
    }

    private async Task<PagedResult<PassengerHistoryItemDto>> GetTicketHistoryAsync(
        GetPassengerHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var outcome = await _bookings.GetPassengerHistoryAsync(
            request.UserId,
            request.Status,
            request.From,
            request.To,
            request.Page,
            request.PageSize,
            cancellationToken);
        if (!outcome.IsSuccess || outcome.Page is null)
        {
            throw new PassengerHistoryUpstreamUnavailableException(
                "Booking history is temporarily unavailable.");
        }

        var items = outcome.Page.Items.Select(booking => new PassengerHistoryItemDto(
            "TICKET",
            booking.BookingId,
            booking.BookingCode,
            booking.TripId,
            booking.Status,
            booking.CreatedAt,
            booking.TotalAmount,
            booking.OriginName,
            booking.DestinationName,
            booking.DepartureDateTime,
            null,
            new TicketHistoryDetailsDto(
                booking.BookingGroupId,
                booking.TripDirection,
                booking.RouteName,
                booking.Tickets.Select(ticket => new PassengerHistoryTicketDto(
                    ticket.TicketId,
                    ticket.TicketCode,
                    ticket.SeatNumber,
                    ticket.Status,
                    ticket.PaidAmount)).ToList()),
            null))
            .ToList();

        return PagedResult<PassengerHistoryItemDto>.Create(
            items,
            outcome.Page.Page,
            outcome.Page.PageSize,
            outcome.Page.TotalItems);
    }
}
