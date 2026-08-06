using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stations;

public static class StationMapper
{
    public static StationDto ToDto(Station station) => new(
        station.Id, station.Name, station.Slug, station.AddressStreet, station.LocationId,
        station.City, station.Ward, station.Latitude, station.Longitude,
        station.ContactPhone, station.ContactEmail, station.OperatingHours, station.Facilities,
        station.SupportsShuttle, station.IsActive, station.CreatedAt, station.UpdatedAt);

    public static StationSearchResult ToSearchResult(Station station) => new(
        station.Id,
        station.Name,
        station.LocationId,
        station.City,
        station.Ward,
        station.Latitude,
        station.Longitude,
        station.AddressStreet,
        station.SupportsShuttle);
}
