using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed class ScanBookingCodeForTripQueryHandler
    : IRequestHandler<ScanBookingCodeForTripQuery, ScanBookingCodeForTripResult>
{
    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripServiceClient;

    public ScanBookingCodeForTripQueryHandler(
        IBookingRepository bookings,
        ITripServiceClient tripServiceClient)
    {
        _bookings = bookings;
        _tripServiceClient = tripServiceClient;
    }

    public async Task<ScanBookingCodeForTripResult> Handle(
        ScanBookingCodeForTripQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await _tripServiceClient.GetTripSnapshotAsync(
            request.TripId,
            cancellationToken);

        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        }

        if (request.CallerUserId != trip.DriverUserId
            && request.CallerUserId != trip.AssistantUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Caller is not assigned to this trip.");
        }

        var booking = !string.IsNullOrWhiteSpace(request.TicketCode)
            ? await _bookings.FindByTicketCodeWithPassengersAsync(
                request.TicketCode,
                cancellationToken)
            : await _bookings.FindByBookingCodeAsync(
                request.BookingCode!,
                cancellationToken);

        if (booking is null)
        {
            throw BookingNotFound();
        }

        if (booking.TripId != request.TripId)
        {
            throw new CodedValidationException(
                "BOOKING_NOT_FOR_THIS_TRIP",
                "Booking does not belong to this trip.");
        }

        if (booking.Status != BookingStatus.CONFIRMED)
        {
            throw BookingNotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TicketCode))
        {
            booking = await _bookings.FindByIdWithPassengersAsync(
                booking.Id,
                cancellationToken);

            if (booking is null)
            {
                throw BookingNotFound();
            }
        }

        var items = !string.IsNullOrWhiteSpace(request.TicketCode)
            ? BuildTicketCodeItems(booking, request.TicketCode, CanExposeBuyerContact(trip.Status))
            : BuildLegacyBookingCodeItems(booking, CanExposeBuyerContact(trip.Status));

        if (items.Count == 0)
        {
            throw BookingNotFound();
        }

        return new ScanBookingCodeForTripResult(items);
    }

    private static IReadOnlyList<ScanBookingCodePassengerItem> BuildTicketCodeItems(
        VietRide.Booking.Domain.Entities.Booking booking,
        string ticketCode,
        bool exposeBuyerContact)
    {
        var ticket = booking.Tickets.SingleOrDefault(candidate =>
            string.Equals(candidate.TicketCode.Value, ticketCode, StringComparison.OrdinalIgnoreCase));

        if (ticket is null || !IsBoardableTicket(ticket.Status))
        {
            return [];
        }

        var passenger = booking.Passengers.SingleOrDefault(candidate =>
            candidate.Id == ticket.PassengerId);

        return passenger is null
            ? []
            :
            [
                new ScanBookingCodePassengerItem(
                    passenger.Id,
                    ticket.Id,
                    ticket.TicketCode.Value,
                    ticket.SeatNumber,
                    passenger.BoardingStatus.ToString(),
                    booking.BookingCode.Value,
                    GetBuyerName(booking.BuyerDisplayName, exposeBuyerContact),
                    GetBuyerPhone(
                        booking.BuyerDisplayName,
                        booking.BuyerPhone,
                        exposeBuyerContact)),
            ];
    }

    private static IReadOnlyList<ScanBookingCodePassengerItem> BuildLegacyBookingCodeItems(
        VietRide.Booking.Domain.Entities.Booking booking,
        bool exposeBuyerContact)
    {
        var passengersById = booking.Passengers.ToDictionary(passenger => passenger.Id);

        return booking.Tickets
            .Where(ticket => IsBoardableTicket(ticket.Status))
            .OrderBy(ticket => ticket.SeatNumber, StringComparer.Ordinal)
            .Select(ticket => new
            {
                Ticket = ticket,
                HasPassenger = passengersById.TryGetValue(ticket.PassengerId, out var passenger),
                Passenger = passenger,
            })
            .Where(entry => entry.HasPassenger && entry.Passenger is not null)
            .Select(entry => new ScanBookingCodePassengerItem(
                entry.Passenger!.Id,
                entry.Ticket.Id,
                entry.Ticket.TicketCode.Value,
                entry.Ticket.SeatNumber,
                entry.Passenger.BoardingStatus.ToString(),
                booking.BookingCode.Value,
                GetBuyerName(booking.BuyerDisplayName, exposeBuyerContact),
                GetBuyerPhone(
                    booking.BuyerDisplayName,
                    booking.BuyerPhone,
                    exposeBuyerContact)))
            .ToArray();
    }

    private static bool CanExposeBuyerContact(string tripStatus)
        => tripStatus is "BOARDING" or "IN_PROGRESS";

    private static string? GetBuyerName(string? buyerDisplayName, bool exposeBuyerContact)
    {
        if (!exposeBuyerContact
            || string.IsNullOrWhiteSpace(buyerDisplayName)
            || string.Equals(
                buyerDisplayName,
                BookingBuyerSnapshotProfile.DeletedDisplayName,
                StringComparison.Ordinal))
        {
            return null;
        }

        return buyerDisplayName;
    }

    private static string? GetBuyerPhone(
        string? buyerDisplayName,
        string? buyerPhone,
        bool exposeBuyerContact)
        => exposeBuyerContact && !IsRedactedBuyerSnapshot(buyerDisplayName)
            ? buyerPhone
            : null;

    private static bool IsRedactedBuyerSnapshot(string? buyerDisplayName)
        => string.Equals(
            buyerDisplayName,
            BookingBuyerSnapshotProfile.DeletedDisplayName,
            StringComparison.Ordinal);

    private static bool IsBoardableTicket(TicketStatus status)
        => status is TicketStatus.ISSUED or TicketStatus.USED;

    private static CodedNotFoundException BookingNotFound()
        => new("BOOKING_NOT_FOUND", "Booking not found.");
}
