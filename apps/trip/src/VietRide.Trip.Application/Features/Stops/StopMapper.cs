using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stops;

internal static class StopMapper
{
    public static StopDto ToDto(Stop stop)
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
            stop.LocationId);
}
