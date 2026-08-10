using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stops;

internal static class StopMapper
{
    public static StopDto ToDto(Stop stop, StopLocationContext? location = null)
        => new(
            stop.Id,
            stop.OperatorId,
            stop.Name,
            stop.Description,
            stop.Latitude,
            stop.Longitude,
            stop.Address,
            stop.GooglePlaceId,
            stop.IsActive,
            stop.CreatedAt,
            stop.UpdatedAt,
            stop.LocationId,
            location?.City,
            location?.Ward);

    public static StopDto ToDto(
        Stop stop,
        IReadOnlyDictionary<Guid, StopLocationContext> locations)
        => ToDto(
            stop,
            stop.LocationId.HasValue && locations.TryGetValue(stop.LocationId.Value, out var location)
                ? location
                : null);
}
