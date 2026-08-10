namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed record SearchTripPointDto(
    string Type,
    Guid? StationId,
    Guid? StopId,
    string Name,
    string? Address,
    int OrderIndex,
    DateTimeOffset EstimatedTime,
    bool AllowPickup,
    bool AllowDropoff);
