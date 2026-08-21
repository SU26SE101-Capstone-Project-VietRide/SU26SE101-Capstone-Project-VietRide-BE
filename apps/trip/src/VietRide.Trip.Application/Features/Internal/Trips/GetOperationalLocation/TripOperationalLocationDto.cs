namespace VietRide.Trip.Application.Features.Internal.Trips.GetOperationalLocation;

public sealed record TripOperationalLocationDto(
    Guid TripId,
    Guid VehicleId,
    string TripStatus,
    Guid? CurrentStopId,
    string? CurrentStopStatus,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt,
    DateTimeOffset? DestinationArrivedAt);
