using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.ValueObjects;

namespace VietRide.Booking.Application.Features.Bookings;

internal static class BookingPointSnapshotFactory
{
    public static BookingPointSnapshot ResolvePickup(
        TripSnapshot trip,
        Guid? stationId,
        Guid? stopId)
        => Resolve(
            trip,
            stationId,
            stopId,
            trip.OriginStation.Id,
            trip.OriginStation.Name,
            trip.DepartureDateTime);

    public static BookingPointSnapshot ResolveDropoff(
        TripSnapshot trip,
        Guid? stationId,
        Guid? stopId)
        => Resolve(
            trip,
            stationId ?? (stopId.HasValue ? null : trip.DestinationStation.Id),
            stopId,
            trip.DestinationStation.Id,
            trip.DestinationStation.Name,
            trip.EstimatedArrivalTime);

    private static BookingPointSnapshot Resolve(
        TripSnapshot trip,
        Guid? stationId,
        Guid? stopId,
        Guid defaultStationId,
        string defaultStationName,
        DateTimeOffset defaultStationPlannedAt)
    {
        if (stopId.HasValue)
        {
            var stop = trip.Stops.Single(item => item.StopId == stopId.Value);
            return new BookingPointSnapshot(
                BookingPointSnapshot.StopType,
                stop.StopId,
                stop.Name,
                null,
                stop.EstimatedArrivalTime);
        }

        var selectedStationId = stationId ?? defaultStationId;
        if (selectedStationId == trip.OriginStation.Id)
        {
            return new BookingPointSnapshot(
                BookingPointSnapshot.StationType,
                selectedStationId,
                trip.OriginStation.Name,
                null,
                trip.DepartureDateTime);
        }

        if (selectedStationId == trip.DestinationStation.Id)
        {
            return new BookingPointSnapshot(
                BookingPointSnapshot.StationType,
                selectedStationId,
                trip.DestinationStation.Name,
                null,
                trip.EstimatedArrivalTime);
        }

        return new BookingPointSnapshot(
            BookingPointSnapshot.StationType,
            selectedStationId,
            defaultStationName,
            null,
            defaultStationPlannedAt);
    }
}
