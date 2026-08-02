namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record ShuttleTrackingStation(
    Guid StationId,
    string Name,
    decimal? Latitude,
    decimal? Longitude,
    int PickupOrder);
