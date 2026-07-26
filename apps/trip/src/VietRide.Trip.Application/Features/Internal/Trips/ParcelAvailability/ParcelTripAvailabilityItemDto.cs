namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed record ParcelTripAvailabilityItemDto(
    Guid TripId,
    Guid RouteId,
    Guid OperatorId,
    string OperatorName,
    string Status,
    ParcelTripStationDto OriginStation,
    ParcelTripStationDto DestinationStation,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    decimal AvailableCargoWeightKg,
    decimal AvailableCargoVolumeM3);
