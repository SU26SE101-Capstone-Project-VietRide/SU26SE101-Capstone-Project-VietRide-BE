using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stations;

internal static class StationMapper
{
    public static StationSearchResult ToSearchResult(Station station) => new(
        station.Id,
        station.Name,
        station.LocationId,
        station.City,
        station.Province,
        station.Latitude,
        station.Longitude,
        station.AddressStreet,
        station.SupportsShuttle);
}
