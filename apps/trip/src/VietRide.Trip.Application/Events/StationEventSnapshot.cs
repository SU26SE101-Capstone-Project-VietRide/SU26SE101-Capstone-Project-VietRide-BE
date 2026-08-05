using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Events;

public sealed record StationEventSnapshot(
    Guid Id,
    string Name,
    string Slug,
    string City,
    string? Ward,
    decimal? Latitude,
    decimal? Longitude,
    bool SupportsShuttle,
    bool IsActive)
{
    public static StationEventSnapshot FromStation(Station station)
        => new(
            station.Id,
            station.Name,
            station.Slug,
            station.City,
            station.Ward,
            station.Latitude,
            station.Longitude,
            station.SupportsShuttle,
            station.IsActive);
}
