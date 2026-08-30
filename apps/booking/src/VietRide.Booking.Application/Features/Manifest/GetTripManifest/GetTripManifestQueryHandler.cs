using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Manifest.GetTripManifest;

public sealed class GetTripManifestQueryHandler
    : IRequestHandler<GetTripManifestQuery, GetTripManifestResult>
{
    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripServiceClient;

    public GetTripManifestQueryHandler(
        IBookingRepository bookings,
        ITripServiceClient tripServiceClient)
    {
        _bookings = bookings;
        _tripServiceClient = tripServiceClient;
    }

    public async Task<GetTripManifestResult> Handle(
        GetTripManifestQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await _tripServiceClient.GetTripSnapshotAsync(
            request.TripId,
            cancellationToken);

        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        }

        if (trip.DriverUserId != request.CallerUserId
            && trip.AssistantUserId != request.CallerUserId)
        {
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this trip.");
        }

        var pickupOrderByStopId = trip.Stops.ToDictionary(
            stop => stop.StopId,
            stop => stop.OrderIndex);
        var pickupNameByStopId = trip.Stops.ToDictionary(
            stop => stop.StopId,
            stop => stop.Name);
        var exposeBuyerContact = CanExposeBuyerContact(trip.Status);

        var confirmedBookings = _bookings.QueryNoTracking()
            .Where(booking => booking.TripId == request.TripId
                && (booking.Status == BookingStatus.CONFIRMED
                    || booking.Status == BookingStatus.PARTIAL_NO_SHOW
                    || booking.Status == BookingStatus.NO_SHOW))
            .Select(booking => new
            {
                booking.BookingCode,
                booking.PickupStopId,
                booking.BuyerDisplayName,
                booking.BuyerPhone,
                Passengers = booking.Passengers
                    .Select(passenger => new
                    {
                        passenger.Id,
                        passenger.SeatNumber,
                        passenger.BoardingStatus,
                    })
                    .ToArray(),
                Tickets = booking.Tickets
                    .Where(ticket => ticket.Status == TicketStatus.ISSUED || ticket.Status == TicketStatus.USED)
                    .Select(ticket => new
                    {
                        ticket.Id,
                        ticket.PassengerId,
                        ticket.TicketCode,
                    })
                    .ToArray(),
            })
            .ToList();

        var items = confirmedBookings
            .SelectMany(booking =>
            {
                var passengersById = booking.Passengers.ToDictionary(passenger => passenger.Id);

                return booking.Tickets
                    .Where(ticket => passengersById.ContainsKey(ticket.PassengerId))
                    .Select(ticket =>
                    {
                        var passenger = passengersById[ticket.PassengerId];

                        return new
                        {
                            Item = new GetTripManifestItem(
                                passenger.Id,
                                ticket.Id,
                                ticket.TicketCode.Value,
                                passenger.SeatNumber!,
                                booking.BookingCode.Value,
                                booking.PickupStopId,
                                passenger.BoardingStatus.ToString(),
                                GetPickupPointName(
                                    booking.PickupStopId,
                                    trip.OriginStation.Name,
                                    pickupNameByStopId),
                                GetBuyerName(booking.BuyerDisplayName, exposeBuyerContact),
                                GetBuyerPhone(
                                    booking.BuyerDisplayName,
                                    booking.BuyerPhone,
                                    exposeBuyerContact)),
                            PickupOrder = GetPickupOrder(booking.PickupStopId, pickupOrderByStopId),
                        };
                    });
            })
            .OrderBy(entry => entry.PickupOrder)
            .ThenBy(entry => entry.Item.SeatNumber, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.BookingCode, StringComparer.Ordinal)
            .Select(entry => entry.Item)
            .ToArray();

        return new GetTripManifestResult(items);
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

    private static string? GetPickupPointName(
        Guid? pickupStopId,
        string originStationName,
        IReadOnlyDictionary<Guid, string?> pickupNameByStopId)
    {
        if (pickupStopId is null)
        {
            return originStationName;
        }

        return pickupNameByStopId.GetValueOrDefault(pickupStopId.Value);
    }

    private static int GetPickupOrder(
        Guid? pickupStopId,
        IReadOnlyDictionary<Guid, int> pickupOrderByStopId)
    {
        if (pickupStopId is null)
        {
            return 0;
        }

        return pickupOrderByStopId.TryGetValue(pickupStopId.Value, out var orderIndex)
            ? orderIndex
            : int.MaxValue;
    }
}
