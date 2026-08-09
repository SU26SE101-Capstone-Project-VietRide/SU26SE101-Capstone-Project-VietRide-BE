using System.Text.Json;
using VietRide.Trip.Application.Abstractions.Services;
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
        IReadOnlyCollection<TripStop> stops,
        FareSurchargeAdjustment fareAdjustment)
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
            stops.Any(stop => stop.AllowDropoff))
        {
            SurchargePercent = fareAdjustment.SurchargePercent,
            SurchargeAmount = fareAdjustment.SurchargeAmount,
            EffectiveFare = fareAdjustment.EffectiveFare,
            SurchargePeriodId = fareAdjustment.SurchargePeriodId,
            SurchargePeriodName = fareAdjustment.SurchargePeriodName,
        };
    }

    public static TripDetailDto ToTripDetailDto(
        Domain.Entities.Trip trip,
        Route route,
        Station originStation,
        Station destinationStation,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyCollection<TripStop> stops,
        IReadOnlyDictionary<Guid, Stop> stopDetails,
        IReadOnlyDictionary<Guid, long> fares,
        FareSurchargeAdjustment baseFareAdjustment,
        IReadOnlyDictionary<Guid, FareSurchargeAdjustment> fareAdjustments)
    {
        var stopDtos = stops
            .OrderBy(stop => stop.OrderIndex)
            .Select(stop =>
            {
                if (!stopDetails.TryGetValue(stop.StopId, out var details))
                {
                    throw new InvalidOperationException($"Trip stop '{stop.StopId}' has no canonical Stop row.");
                }

                var fareOverride = fares.TryGetValue(stop.StopId, out var fare) ? fare : (long?)null;
                var originalFare = fareOverride ?? trip.BaseFare.Amount;
                var adjustment = fareAdjustments.GetValueOrDefault(stop.StopId, baseFareAdjustment);
                return new TripStopDto(
                    stop.StopId,
                    details.Name,
                    details.Address,
                    details.Latitude,
                    details.Longitude,
                    details.IsActive && details.DeletedAt is null,
                    stop.OrderIndex,
                    stop.AllowPickup,
                    stop.AllowDropoff,
                    stop.Status.ToString(),
                    stop.EstimatedArrivalTime,
                    stop.ActualArrivalTime,
                    stop.DistanceFromOriginKm.HasValue ? (double)stop.DistanceFromOriginKm.Value : null,
                    fareOverride,
                    adjustment.EffectiveFare)
                {
                    SurchargePercent = adjustment.SurchargePercent,
                    SurchargeAmount = checked(adjustment.EffectiveFare - originalFare),
                    SurchargePeriodId = adjustment.SurchargePeriodId,
                    SurchargePeriodName = adjustment.SurchargePeriodName,
                };
            })
            .ToList();

        return new TripDetailDto(
            trip.Id,
            trip.OperatorId,
            trip.RouteId,
            trip.VehicleId,
            trip.Status.ToString(),
            trip.DepartureDateTime,
            trip.EstimatedArrivalTime,
            trip.DestinationArrivedAt,
            trip.BaseFare.Amount,
            new TripStationDto(originStation.Id, originStation.Name),
            new TripStationDto(destinationStation.Id, destinationStation.Name),
            stopDtos,
            new TripSeatSummaryDto(seats.Count, CountAvailableSeats(seats)),
            route.ReturnRouteId,
            new TripFareBreakdownDto(
                trip.BaseFare.Amount,
                fares.Select(fare => new TripFareStopDto(fare.Key, fare.Value)
                {
                    SurchargePercent = fareAdjustments[fare.Key].SurchargePercent,
                    SurchargeAmount = fareAdjustments[fare.Key].SurchargeAmount,
                    EffectiveFareFromThisStop = fareAdjustments[fare.Key].EffectiveFare,
                }).ToList())
            {
                SurchargePercent = baseFareAdjustment.SurchargePercent,
                SurchargeAmount = baseFareAdjustment.SurchargeAmount,
                EffectiveBaseFare = baseFareAdjustment.EffectiveFare,
                SurchargePeriodId = baseFareAdjustment.SurchargePeriodId,
                SurchargePeriodName = baseFareAdjustment.SurchargePeriodName,
            })
        {
            PlannedEtaQuality = trip.PlannedEtaSource == PlannedEtaSource.GOOGLE_ROUTES
                ? "TRAFFIC_AWARE"
                : "FALLBACK",
            SurchargePercent = baseFareAdjustment.SurchargePercent,
            SurchargeAmount = baseFareAdjustment.SurchargeAmount,
            EffectiveFare = baseFareAdjustment.EffectiveFare,
            SurchargePeriodId = baseFareAdjustment.SurchargePeriodId,
            SurchargePeriodName = baseFareAdjustment.SurchargePeriodName,
        };
    }

    public static TripSeatMapDto ToTripSeatMapDto(
        Domain.Entities.Trip trip,
        Vehicle vehicle,
        VehicleType? vehicleType,
        IReadOnlyCollection<TripSeat> seats)
    {
        var layoutJson = trip.SeatLayoutSnapshotJson ?? vehicle.SeatLayoutJson;
        var layout = layoutJson.Deserialize<SeatLayoutDto>()
            ?? throw new InvalidOperationException("Stored vehicle seat layout is invalid.");
        var layoutSeats = layout.Seats.ToDictionary(seat => seat.SeatNumber, StringComparer.OrdinalIgnoreCase);
        var seatDtos = seats
            .OrderBy(seat => seat.SeatNumber, StringComparer.OrdinalIgnoreCase)
            .Select(seat => ToSeatMapSeatDto(seat, layoutSeats))
            .ToList();

        return new TripSeatMapDto(
            trip.Id,
            trip.SeatLayoutSnapshotJson.HasValue
                ? layout.VehicleTypeCode
                : vehicleType?.Code ?? layout.VehicleTypeCode,
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
            layoutSeat.Deck,
            seat.DisabledReason);
    }

    private static int CountAvailableSeats(IEnumerable<TripSeat> seats) =>
        seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE);
}
