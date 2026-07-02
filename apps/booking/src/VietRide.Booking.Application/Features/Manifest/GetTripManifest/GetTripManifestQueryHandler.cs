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

        var confirmedBookings = _bookings.QueryNoTracking()
            .Where(booking => booking.TripId == request.TripId
                && booking.Status == BookingStatus.CONFIRMED)
            .Select(booking => new
            {
                booking.BookingCode,
                booking.PickupStopId,
                Passengers = booking.Passengers
                    .Select(passenger => new
                    {
                        passenger.SeatNumber,
                        passenger.BoardingStatus,
                    })
                    .ToArray(),
            })
            .ToList();

        var items = confirmedBookings
            .SelectMany(booking => booking.Passengers.Select(passenger => new
            {
                Item = new GetTripManifestItem(
                    passenger.SeatNumber,
                    booking.BookingCode.Value,
                    booking.PickupStopId,
                    passenger.BoardingStatus.ToString()),
                PickupOrder = GetPickupOrder(booking.PickupStopId, pickupOrderByStopId),
            }))
            .OrderBy(entry => entry.PickupOrder)
            .ThenBy(entry => entry.Item.SeatNumber, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.BookingCode, StringComparer.Ordinal)
            .Select(entry => entry.Item)
            .ToArray();

        return new GetTripManifestResult(items);
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
