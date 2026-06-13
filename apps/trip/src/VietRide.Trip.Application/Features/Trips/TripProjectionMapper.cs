using System.Text.Json;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.SearchTrips;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.Application.Features.Trips;

internal static class TripProjectionMapper
{
    public static SearchTripItem ToSearchTripItem(
        Domain.Entities.Trip trip,
        Route route,
        string operatorName,
        Station originStation,
        Station destinationStation,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyCollection<TripStop> stops)
    {
        return new SearchTripItem(
            trip.Id,
            trip.OperatorId,
            operatorName,
            route.Id,
            trip.DepartureDateTime,
            trip.EstimatedArrivalTime,
            new SearchTripStationDto(originStation.Id, originStation.Name),
            new SearchTripStationDto(destinationStation.Id, destinationStation.Name),
            CountAvailableSeats(seats),
            trip.BaseFare.Amount,
            stops.Any(stop => stop.AllowPickup),
            stops.Any(stop => stop.AllowDropoff));
    }

    public static TripDetailDto ToTripDetailDto(
        Domain.Entities.Trip trip,
        Route route,
        Station originStation,
        Station destinationStation,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyCollection<TripStop> stops,
        IReadOnlyDictionary<Guid, long> fares)
    {
        var stopDtos = stops
            .OrderBy(stop => stop.OrderIndex)
            .Select(stop => new TripStopDto(
                stop.StopId,
                stop.OrderIndex,
                stop.AllowPickup,
                stop.AllowDropoff,
                stop.EstimatedArrivalTime,
                stop.DistanceFromOriginKm.HasValue ? (double)stop.DistanceFromOriginKm.Value : null,
                fares.TryGetValue(stop.StopId, out var fare) ? fare : null))
            .ToList();

        return new TripDetailDto(
            trip.Id,
            trip.OperatorId,
            trip.RouteId,
            trip.VehicleId,
            trip.Status.ToString(),
            trip.DepartureDateTime,
            trip.EstimatedArrivalTime,
            trip.BaseFare.Amount,
            new TripStationDto(originStation.Id, originStation.Name),
            new TripStationDto(destinationStation.Id, destinationStation.Name),
            stopDtos,
            new TripSeatSummaryDto(seats.Count, CountAvailableSeats(seats)),
            route.ReturnRouteId,
            new TripFareBreakdownDto(
                trip.BaseFare.Amount,
                fares.Select(fare => new TripFareStopDto(fare.Key, fare.Value)).ToList()));
    }

    public static TripSeatMapDto ToTripSeatMapDto(
        Domain.Entities.Trip trip,
        Vehicle vehicle,
        VehicleType? vehicleType,
        IReadOnlyCollection<TripSeat> seats)
    {
        var layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>()
            ?? throw new InvalidOperationException("Stored vehicle seat layout is invalid.");
        var layoutSeats = layout.Seats.ToDictionary(seat => seat.SeatNumber, StringComparer.OrdinalIgnoreCase);
        var seatDtos = seats
            .OrderBy(seat => seat.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(seat => ToSeatMapSeatDto(seat, layoutSeats))
            .ToList();

        return new TripSeatMapDto(
            trip.Id,
            vehicleType?.Code ?? layout.VehicleTypeCode,
            seatDtos);
    }

    private static TripSeatMapSeatDto ToSeatMapSeatDto(
        TripSeat seat,
        IReadOnlyDictionary<string, SeatLayoutSeatDto> layoutSeats)
    {
        if (!layoutSeats.TryGetValue(seat.SeatNumber, out var layoutSeat))
        {
            throw new InvalidOperationException($"Seat '{seat.SeatNumber}' is missing from vehicle seat layout.");
        }

        return new TripSeatMapSeatDto(
            seat.SeatNumber,
            seat.Status.ToString(),
            layoutSeat.Type,
            layoutSeat.Row,
            layoutSeat.Col,
            layoutSeat.Deck);
    }

    private static int CountAvailableSeats(IEnumerable<TripSeat> seats) =>
        seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE);
}
