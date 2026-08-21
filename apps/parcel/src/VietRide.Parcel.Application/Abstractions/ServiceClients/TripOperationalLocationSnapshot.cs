namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripOperationalLocationSnapshot(
    Guid TripId,
    Guid VehicleId,
    string TripStatus,
    Guid? CurrentStopId,
    string? CurrentStopStatus,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt,
    DateTimeOffset? DestinationArrivedAt);
